﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.Azure.WebJobs.Script.ChangeAnalysis;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Middleware;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Azure.WebJobs.Script.WebHost.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public static class WebScriptHostBuilderExtension
    {
        public static IHostBuilder AddWebScriptHost(this IHostBuilder builder, IServiceProvider rootServiceProvider,
           IServiceScopeFactory rootScopeFactory, ScriptApplicationHostOptions webHostOptions, Action<IWebJobsBuilder> configureWebJobs = null)
        {
            ILoggerFactory configLoggerFactory = rootServiceProvider.GetService<ILoggerFactory>();
            IDependencyValidator validator = rootServiceProvider.GetService<IDependencyValidator>();
            IMetricsLogger metricsLogger = rootServiceProvider.GetService<IMetricsLogger>();
            IEnvironment environment = rootServiceProvider.GetService<IEnvironment>();

            builder.UseServiceProviderFactory(new JobHostScopedServiceProviderFactory(rootServiceProvider, rootScopeFactory, validator))
                .ConfigureServices(services =>
                {
                    // register default configuration
                    // must happen before the script host is added below
                    services.ConfigureOptions<HttpOptionsSetup>();
                    services.ConfigureOptions<CustomHttpHeadersOptionsSetup>();
                    services.ConfigureOptions<HostHstsOptionsSetup>();
                    services.ConfigureOptions<HostCorsOptionsSetup>();
                    services.ConfigureOptions<CorsOptionsSetup>();
                    services.ConfigureOptions<AppServiceOptionsSetup>();
                    services.ConfigureOptions<HostEasyAuthOptionsSetup>();
                })
                .AddScriptHost(webHostOptions, configLoggerFactory, metricsLogger, webJobsBuilder =>
                {
                    webJobsBuilder.AddAzureStorageCoreServices();

                    configureWebJobs?.Invoke(webJobsBuilder);

                    ConfigureRegisteredBuilders(webJobsBuilder, rootServiceProvider);

                    webJobsBuilder.Services.AddSingleton<IHttpRoutesManager, WebScriptHostHttpRoutesManager>();
                })
                .ConfigureAppConfiguration(configurationBuilder =>
                {
                    ConfigureRegisteredBuilders(configurationBuilder, rootServiceProvider);
                })
                .ConfigureLogging(loggingBuilder =>
                {
                    loggingBuilder.Services.AddSingleton<ILoggerFactory, ScriptLoggerFactory>();

                    loggingBuilder.AddWebJobsSystem<SystemLoggerProvider>();
                    if (environment.IsAzureMonitorEnabled())
                    {
                        loggingBuilder.Services.AddSingleton<ILoggerProvider, AzureMonitorDiagnosticLoggerProvider>();
                    }

                    ConfigureRegisteredBuilders(loggingBuilder, rootServiceProvider);
                })
                .ConfigureServices(services =>
                {
                    var webHostEnvironment = rootServiceProvider.GetService<IScriptWebHostEnvironment>();

                    if (FunctionsSyncManager.IsSyncTriggersEnvironment(webHostEnvironment, environment))
                    {
                        services.AddSingleton<IHostedService, FunctionsSyncService>();
                    }

                    if (!environment.IsV2CompatibilityMode())
                    {
                        new FunctionsMvcBuilder(services).AddNewtonsoftJson();
                    }

                    services.AddSingleton<HttpRequestQueue>();
                    services.AddSingleton<IHostLifetime, JobHostHostLifetime>();
                    services.AddSingleton<IWebJobsExceptionHandler, WebScriptHostExceptionHandler>();

                    services.AddSingleton<DefaultScriptWebHookProvider>();
                    services.TryAddSingleton<IScriptWebHookProvider>(p => p.GetService<DefaultScriptWebHookProvider>());
                    services.TryAddSingleton<IWebHookProvider>(p => p.GetService<DefaultScriptWebHookProvider>());
                    services.TryAddSingleton<IJobHostMiddlewarePipeline, DefaultMiddlewarePipeline>();
                    services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHostHttpMiddleware, CustomHttpHeadersMiddleware>());
                    services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHostHttpMiddleware, HstsConfigurationMiddleware>());
                    if (environment.IsLinuxConsumption())
                    {
                        services.AddSingleton<ICorsMiddlewareFactory, CorsMiddlewareFactory>();
                        services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHostHttpMiddleware, JobHostCorsMiddleware>());

                        // EasyAuth must go after CORS, as CORS preflight requests can happen before authentication
                        services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHostHttpMiddleware, JobHostEasyAuthMiddleware>());
                    }
                    services.TryAddSingleton<IScaleMetricsRepository, TableStorageScaleMetricsRepository>();

                    if (environment.IsWindowsAzureManagedHosting() || environment.IsLinuxAzureManagedHosting())
                    {
                        // Enable breaking change analysis only when hosted in Azure
                        services.AddSingleton<IChangeAnalysisStateProvider, BlobChangeAnalysisStateProvider>();
                        services.AddSingleton<IHostedService, ChangeAnalysisService>();
                    }

                    // Make sure the registered IHostIdProvider is used
                    IHostIdProvider provider = rootServiceProvider.GetService<IHostIdProvider>();
                    if (provider != null)
                    {
                        services.AddSingleton<IHostIdProvider>(provider);
                    }

                    services.AddSingleton<IDelegatingHandlerProvider, DefaultDelegatingHandlerProvider>();

                    // Logging and diagnostics
                    services.AddSingleton<IMetricsLogger>(a => new NonDisposableMetricsLogger(metricsLogger));
                    services.AddSingleton<IEventCollectorProvider, FunctionInstanceLogCollectorProvider>();

                    // Hosted services
                    services.AddSingleton<IFileMonitoringService, FileMonitoringService>();
                    services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, IFileMonitoringService>(p => p.GetService<IFileMonitoringService>()));

                    ConfigureRegisteredBuilders(services, rootServiceProvider);
                });

            var debugStateProvider = rootServiceProvider.GetService<IDebugStateProvider>();
            if (!FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagDisableDevInDebug, environment) && debugStateProvider.InDebugMode)
            {
                builder.UseEnvironment(EnvironmentName.Development);
            }

            return builder;
        }

        private static void ConfigureRegisteredBuilders<TBuilder>(TBuilder builder, IServiceProvider services)
        {
            foreach (IConfigureBuilder<TBuilder> configureBuilder in services.GetServices<IConfigureBuilder<TBuilder>>())
            {
                configureBuilder.Configure(builder);
            }
        }

        /// <summary>
        /// Used internally to register Newtonsoft formatters with our ScriptHost.
        /// </summary>
        private class FunctionsMvcBuilder : IMvcBuilder
        {
            private readonly IServiceCollection _serviceCollection;

            public FunctionsMvcBuilder(IServiceCollection serviceCollection)
            {
                _serviceCollection = serviceCollection;
            }

            public ApplicationPartManager PartManager { get; } = new ApplicationPartManager();

            public IServiceCollection Services => _serviceCollection;
        }
    }
}

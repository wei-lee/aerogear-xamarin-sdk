﻿using System;
using System.Collections.Generic;
using AeroGear.Mobile.Core.Configuration;
using AeroGear.Mobile.Core.Logging;
using AeroGear.Mobile.Core.Exception;
using System.Diagnostics.Contracts;
using AeroGear.Mobile.Core.Http;
using System.Reflection;
using System.Net.Http;

namespace AeroGear.Mobile.Core
{
    /// <summary>
    /// MobileCore is the entry point into AeroGear mobile services.
    /// </summary>
    public sealed class MobileCore
    {

        public const String DEFAULT_CONFIG_FILE_NAME = "mobile-services.json";

        private static String TAG = "AEROGEAR/CORE";

        private static int DEFAULT_TIMEOUT = 20;

        public ILogger Logger
        {
            get;
            private set;
        }

        public String ConfigFileName
        {
            get;
            private set;
        }

        private static MobileCore instance;

        /// <summary>
        /// Holds MobileCore singleton instance. It's needed to initialize core before using this.
        /// </summary>
        public static MobileCore Instance
        {
            get
            {
                if (instance == null)
                {
                    throw new InitializationException("Core is not initialized, don't forget to call MobileCore.init() before using this instance.");
                }

                return instance;
            }
        }

        /// <summary>
        /// Gets HTTP service module of the MobileCore.
        /// </summary>
        public IHttpServiceModule HttpLayer
        {
            get; private set;
        }

        private Dictionary<String, ServiceConfiguration> servicesConfig;

        private Dictionary<Type, IServiceModule> services = new Dictionary<Type, IServiceModule>();

        private MobileCore(IPlatformInjector injector, Options options)
        {
            Contract.Requires(options != null);
            Logger = options.Logger ?? injector?.CreateLogger() ?? new NullLogger();

            if (injector != null && options.ConfigFileName != null)
            {
                var filename = $"{injector.DefaultResources}.{options.ConfigFileName}";
                try
                {
                    using (var stream = injector.ExecutingAssembly.GetManifestResourceStream(filename))
                    {
                        servicesConfig = MobileCoreJsonParser.Parse(stream);
                    }
                }
                catch (System.Exception e)
                {
                    throw new InitializationException($"{filename} could not be loaded", e);
                }
            }
            else
            {
                if (options.ConfigJson != null)
                {
                    try
                    {
                        MobileCoreJsonParser.Parse(options.ConfigJson);
                    }
                    catch (System.Exception e)
                    {
                        throw new InitializationException("invalid JSON configuration file", e);
                    }
                }
                else
                    throw new InitializationException("Must provide either filename or JSON configuration in Init() options");
            }

            if (options.HttpServiceModule == null)
            {
                HttpClient httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(DEFAULT_TIMEOUT);
                var httpServiceModule = new SystemNetHttpServiceModule(httpClient);
                var configuration = GetServiceConfiguration(httpServiceModule.Type);
                if (configuration == null)
                {
                    configuration = ServiceConfiguration.Builder.Build();
                }
                httpServiceModule.Configure(this, configuration);
                HttpLayer = httpServiceModule;
            }
            else HttpLayer = options.HttpServiceModule;
        }

        /// <summary>
        /// Initializes MobileCore with defaults and without platform-specific injector. 
        /// </summary>
        /// <returns>MobileCore singleton instance</returns>
        public static MobileCore Init() => Init(null, new Options());

        /// <summary>
        /// Initializes MobileCore with defaults and with platform-specific injector. 
        /// </summary>
        /// <param name="injector">platform specific implementation dependency injection module</param>
        /// <returns>MobileCore singleton instance</returns>
        public static MobileCore Init(IPlatformInjector injector) => Init(injector, new Options());

        /// <summary>
        /// Initializes MobileCore with specific options and with platform-specific injector. 
        /// </summary>
        /// <param name="injector">platform specific implementation dependency injection module</param>
        /// <param name="options">initialization options</param>
        /// <returns>MobileCore singleton instance</returns>
        public static MobileCore Init(IPlatformInjector injector, Options options)
        {
            Contract.Requires(options != null);
            instance = new MobileCore(injector, options);
            return instance;
        }

        /// <summary>
        /// Called when mobile core instance needs to be destroyed.
        /// </summary>
        public void Destroy()
        {
            foreach (var serviceModule in services.Values)
            {
                serviceModule.Destroy();
            }
            instance = null;
        }

        /// <summary>
        /// Returns instance of a service module.
        /// </summary>
        /// <typeparam name="T">service module type</typeparam>
        /// <returns></returns>
        public T GetInstance<T>() where T : IServiceModule => GetInstance<T>(typeof(T), null);

        /// <summary>
        /// Returns instance of a service module.
        /// </summary>
        /// <typeparam name="T">service module type</typeparam>
        /// <param name="serviceConfiguration">service configuration</param>
        /// <returns></returns>
        public T GetInstance<T>(ServiceConfiguration serviceConfiguration) where T : IServiceModule => GetInstance<T>(typeof(T), serviceConfiguration);

        /// <summary>
        /// Returns instance of a service module.
        /// </summary>
        /// <typeparam name="T">service module type</typeparam>
        /// <param name="serviceClass">service module class type</param>
        /// <param name="serviceConfiguration">service configuration</param>
        /// <returns></returns>
        private T GetInstance<T>(Type serviceClass, ServiceConfiguration serviceConfiguration)
            where T : IServiceModule
        {
            Contract.Requires(serviceClass != null);
            if (services.ContainsKey(serviceClass))
            {
                return (T)services[serviceClass];
            }

            IServiceModule serviceModule = Activator.CreateInstance(serviceClass) as IServiceModule;
            var serviceCfg = serviceConfiguration;
            if (serviceCfg == null) serviceCfg = GetServiceConfiguration(serviceModule.Type);
            if (serviceCfg == null && serviceModule.RequiresConfiguration) throw new ConfigurationNotFoundException($"{serviceModule.Type} not found on " + ConfigFileName);
            serviceModule.Configure(this, serviceCfg);
            services[serviceClass] = serviceModule;
            return (T)serviceModule;
        }

        public ServiceConfiguration GetServiceConfiguration(String type)
        {
            return servicesConfig.ContainsKey(type) ? servicesConfig[type] : null;
        }
    }
}


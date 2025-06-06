﻿using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Refit
{
    /// <summary>
    /// HttpClientFactoryExtensions.
    /// </summary>
    public static class HttpClientFactoryExtensions
    {
        /// <summary>
        /// Adds a Refit client to the DI container
        /// </summary>
        /// <typeparam name="T">Type of the Refit interface</typeparam>
        /// <param name="services">container</param>
        /// <param name="settings">Optional. Settings to configure the instance with</param>
        /// <returns></returns>
        public static IHttpClientBuilder AddRefitClient<T>(
            this IServiceCollection services,
            RefitSettings? settings = null
        )
            where T : class
        {
            return AddRefitClient<T>(services, _ => settings);
        }

        /// <summary>
        /// Adds a Refit client to the DI container
        /// </summary>
        /// <typeparam name="T">Type of the Refit interface</typeparam>
        /// <param name="services">container</param>
        /// <param name="serviceKey">An optional key to associate with the specific Refit client instance</param>
        /// <param name="settings">Optional. Settings to configure the instance with</param>
        /// <returns></returns>
        public static IHttpClientBuilder AddKeyedRefitClient<T>(
            this IServiceCollection services,
            object? serviceKey,
            RefitSettings? settings = null
        )
            where T : class
        {
            return AddKeyedRefitClient<T>(services, serviceKey, _ => settings);
        }

        /// <summary>
        /// Adds a Refit client to the DI container
        /// </summary>
        /// <param name="services">container</param>
        /// <param name="refitInterfaceType">Type of the Refit interface</param>
        /// <param name="settings">Optional. Settings to configure the instance with</param>
        /// <returns></returns>
        public static IHttpClientBuilder AddRefitClient(
            this IServiceCollection services,
            Type refitInterfaceType,
            RefitSettings? settings = null
        )
        {
            return AddRefitClient(services, refitInterfaceType, _ => settings);
        }

        /// <summary>
        /// Adds a Refit client to the DI container
        /// </summary>
        /// <param name="services">container</param>
        /// <param name="refitInterfaceType">Type of the Refit interface</param>
        /// <param name="serviceKey">An optional key to associate with the specific Refit client instance</param>
        /// <param name="settings">Optional. Settings to configure the instance with</param>
        /// <returns></returns>
        public static IHttpClientBuilder AddKeyedRefitClient(
            this IServiceCollection services,
            Type refitInterfaceType,
            object? serviceKey,
            RefitSettings? settings = null
        )
        {
            return AddKeyedRefitClient(services, refitInterfaceType, serviceKey, _ => settings);
        }

        /// <summary>
        /// Adds a Refit client to the DI container
        /// </summary>
        /// <typeparam name="T">Type of the Refit interface</typeparam>
        /// <param name="services">container</param>
        /// <param name="settingsAction">Optional. Action to configure refit settings.  This method is called once and only once, avoid using any scoped dependencies that maybe be disposed automatically.</param>
        /// <param name="httpClientName">Optional. Allows users to change the HttpClient name as provided to IServiceCollection.AddHttpClient. Useful for logging scenarios.</param>
        /// <returns></returns>
        public static IHttpClientBuilder AddRefitClient<T>(
            this IServiceCollection services,
            Func<IServiceProvider, RefitSettings?>? settingsAction,
            string? httpClientName = null
        )
            where T : class
        {
            services.AddSingleton(provider => new SettingsFor<T>(settingsAction?.Invoke(provider)));
            services.AddSingleton(
                provider =>
                    RequestBuilder.ForType<T>(
                        provider.GetRequiredService<SettingsFor<T>>().Settings
                    )
            );

            return services
                .AddHttpClient(httpClientName ?? UniqueName.ForType<T>())
                .ConfigureHttpMessageHandlerBuilder(builder =>
                {
                    // check to see if user provided custom auth token
                    if (
                        CreateInnerHandlerIfProvided(
                            builder.Services.GetRequiredService<SettingsFor<T>>().Settings
                        ) is
                        { } innerHandler
                    )
                    {
                        builder.PrimaryHandler = innerHandler;
                    }
                })
                .AddTypedClient(
                    (client, serviceProvider) =>
                        RestService.For<T>(
                            client,
                            serviceProvider.GetService<IRequestBuilder<T>>()!
                        )
                );
        }

        /// <summary>
        /// Adds a Refit client to the DI container with a specified service key
        /// </summary>
        /// <typeparam name="T">Type of the Refit interface</typeparam>
        /// <param name="services">container</param>
        /// <param name="serviceKey">An optional key to associate with the specific Refit client instance</param>
        /// <param name="settingsAction">Optional. Action to configure refit settings.  This method is called once and only once, avoid using any scoped dependencies that maybe be disposed automatically.</param>
        /// <param name="httpClientName">Optional. Allows users to change the HttpClient name as provided to IServiceCollection.AddHttpClient. Useful for logging scenarios.</param>
        /// <returns></returns>
        public static IHttpClientBuilder AddKeyedRefitClient<T>(
            this IServiceCollection services,
            object? serviceKey,
            Func<IServiceProvider, RefitSettings?>? settingsAction,
            string? httpClientName = null
        )
            where T : class
        {
            services.AddKeyedSingleton(serviceKey,
                (provider, _) => new SettingsFor<T>(settingsAction?.Invoke(provider)));
            services.AddKeyedSingleton(
                serviceKey,
                (provider, _) =>
                    RequestBuilder.ForType<T>(
                        provider.GetRequiredKeyedService<SettingsFor<T>>(serviceKey).Settings
                    )
            );

            return services
                .AddHttpClient(httpClientName ?? UniqueName.ForType<T>(serviceKey))
                .ConfigurePrimaryHttpMessageHandler(serviceProvider =>
                {
                    var settings = serviceProvider.GetRequiredKeyedService<SettingsFor<T>>(serviceKey).Settings;
                    return
                        settings?.HttpMessageHandlerFactory?.Invoke()
                        ?? new HttpClientHandler();
                })
                .ConfigureAdditionalHttpMessageHandlers((handlers, serviceProvider) =>
                {
                    var settings = serviceProvider.GetRequiredKeyedService<SettingsFor<T>>(serviceKey).Settings;
                    if (settings?.AuthorizationHeaderValueGetter is { } getToken)
                    {
                        handlers.Add(new AuthenticatedHttpClientHandler(null, getToken));
                    }
                })
                .AddKeyedTypedClient(
                    serviceKey,
                    (client, serviceProvider) =>
                        RestService.For<T>(
                            client,
                            serviceProvider.GetKeyedService<IRequestBuilder<T>>(serviceKey)!
                        )
                );
        }

        /// <summary>
        /// Adds a Refit client to the DI container
        /// </summary>
        /// <param name="services">container</param>
        /// <param name="refitInterfaceType">Type of the Refit interface</param>
        /// <param name="settingsAction">Optional. Action to configure refit settings.  This method is called once and only once, avoid using any scoped dependencies that maybe be disposed automatically.</param>
        /// <param name="httpClientName">Optional. Allows users to change the HttpClient name as provided to IServiceCollection.AddHttpClient. Useful for logging scenarios.</param>
        /// <returns></returns>
        public static IHttpClientBuilder AddRefitClient(
            this IServiceCollection services,
            Type refitInterfaceType,
            Func<IServiceProvider, RefitSettings?>? settingsAction,
            string? httpClientName = null
        )
        {
            var settingsType = typeof(SettingsFor<>).MakeGenericType(refitInterfaceType);
            var requestBuilderType = typeof(IRequestBuilder<>).MakeGenericType(refitInterfaceType);
            services.AddSingleton(
                settingsType,
                provider =>
                    Activator.CreateInstance(
                        typeof(SettingsFor<>).MakeGenericType(refitInterfaceType)!,
                        settingsAction?.Invoke(provider)
                    )!
            );
            services.AddSingleton(
                requestBuilderType,
                provider =>
                    RequestBuilderGenericForTypeMethod
                        .MakeGenericMethod(refitInterfaceType)
                        .Invoke(
                            null,
                            new object?[]
                            {
                                ((ISettingsFor)provider.GetRequiredService(settingsType)).Settings
                            }
                        )!
            );

            return services
                .AddHttpClient(httpClientName ?? UniqueName.ForType(refitInterfaceType))
                .ConfigureHttpMessageHandlerBuilder(builder =>
                {
                    // check to see if user provided custom auth token
                    if (
                        CreateInnerHandlerIfProvided(
                            (
                                (ISettingsFor)builder.Services.GetRequiredService(settingsType)
                            ).Settings
                        ) is
                        { } innerHandler
                    )
                    {
                        builder.PrimaryHandler = innerHandler;
                    }
                })
                .AddTypedClient(
                    refitInterfaceType,
                    (client, serviceProvider) =>
                        RestService.For(
                            refitInterfaceType,
                            client,
                            (IRequestBuilder)serviceProvider.GetRequiredService(requestBuilderType)
                        )
                );
        }

        /// <summary>
        /// Adds a Refit client to the DI container with a specified service key
        /// </summary>
        /// <param name="services">container</param>
        /// <param name="refitInterfaceType">Type of the Refit interface</param>
        /// <param name="serviceKey">An optional key to associate with the specific Refit client instance</param>
        /// <param name="settingsAction">Optional. Action to configure refit settings.  This method is called once and only once, avoid using any scoped dependencies that maybe be disposed automatically.</param>
        /// <param name="httpClientName">Optional. Allows users to change the HttpClient name as provided to IServiceCollection.AddHttpClient. Useful for logging scenarios.</param>
        /// <returns></returns>
        public static IHttpClientBuilder AddKeyedRefitClient(
            this IServiceCollection services,
            Type refitInterfaceType,
            object? serviceKey,
            Func<IServiceProvider, RefitSettings?>? settingsAction,
            string? httpClientName = null
        )
        {
            var settingsType = typeof(SettingsFor<>).MakeGenericType(refitInterfaceType);
            var requestBuilderType = typeof(IRequestBuilder<>).MakeGenericType(refitInterfaceType);
            services.AddKeyedSingleton(
                settingsType,
                serviceKey,
                (provider, _) =>
                    Activator.CreateInstance(
                        typeof(SettingsFor<>).MakeGenericType(refitInterfaceType)!,
                        settingsAction?.Invoke(provider)
                    )!
            );
            services.AddKeyedSingleton(
                requestBuilderType,
                serviceKey,
                (provider, _) =>
                    RequestBuilderGenericForTypeMethod
                        .MakeGenericMethod(refitInterfaceType)
                        .Invoke(
                            null,
                            new object?[]
                            {
                                ((ISettingsFor)provider.GetRequiredKeyedService(settingsType, serviceKey)).Settings
                            }
                        )!
            );

            return services
                .AddHttpClient(httpClientName ?? UniqueName.ForType(refitInterfaceType, serviceKey))
                .ConfigurePrimaryHttpMessageHandler(serviceProvider =>
                {
                    var settings = (ISettingsFor)serviceProvider.GetRequiredKeyedService(settingsType, serviceKey);
                    return
                        settings.Settings?.HttpMessageHandlerFactory?.Invoke()
                        ?? new HttpClientHandler();
                })
                .ConfigureAdditionalHttpMessageHandlers((handlers, serviceProvider) =>
                {
                    var settings = (ISettingsFor)serviceProvider.GetRequiredKeyedService(settingsType, serviceKey);
                    if (settings.Settings?.AuthorizationHeaderValueGetter is { } getToken)
                    {
                        handlers.Add(new AuthenticatedHttpClientHandler(null, getToken));
                    }
                })
                .AddKeyedTypedClient(
                    refitInterfaceType,
                    serviceKey,
                    (client, serviceProvider) =>
                        RestService.For(
                            refitInterfaceType,
                            client,
                            (IRequestBuilder)serviceProvider.GetRequiredKeyedService(requestBuilderType, serviceKey)
                        )
                );
        }

        private static readonly MethodInfo RequestBuilderGenericForTypeMethod =
            typeof(RequestBuilder)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(z => z.IsGenericMethodDefinition && z.GetParameters().Length == 1);

        static HttpMessageHandler? CreateInnerHandlerIfProvided(RefitSettings? settings)
        {
            HttpMessageHandler? innerHandler = null;
            if (settings != null)
            {
                if (settings.HttpMessageHandlerFactory != null)
                {
                    innerHandler = settings.HttpMessageHandlerFactory();
                }

                if (settings.AuthorizationHeaderValueGetter != null)
                {
                    innerHandler = new AuthenticatedHttpClientHandler(
                        settings.AuthorizationHeaderValueGetter,
                        innerHandler
                    );
                }
            }

            return innerHandler;
        }

        static IHttpClientBuilder AddTypedClient(
            this IHttpClientBuilder builder,
            Type type,
            Func<HttpClient, IServiceProvider, object> factory
        )
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            builder.Services.AddTransient(
                type,
                s =>
                {
                    var httpClientFactory = s.GetRequiredService<IHttpClientFactory>();
                    var httpClient = httpClientFactory.CreateClient(builder.Name);

                    return factory(httpClient, s);
                }
            );

            return builder;
        }

        static IHttpClientBuilder AddKeyedTypedClient(
            this IHttpClientBuilder builder,
            Type type,
            object? serviceKey,
            Func<HttpClient, IServiceProvider, object> factory
        )
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            builder.Services.AddKeyedTransient(
                type,
                serviceKey,
                (s, _) =>
                {
                    var httpClientFactory = s.GetRequiredService<IHttpClientFactory>();
                    var httpClient = httpClientFactory.CreateClient(builder.Name);

                    return factory(httpClient, s);
                }
            );

            return builder;
        }

        static IHttpClientBuilder AddKeyedTypedClient<T>(
            this IHttpClientBuilder builder,
            object? serviceKey,
            Func<HttpClient, IServiceProvider, T> factory
        )
            where T : class
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            builder.Services.AddKeyedTransient(
                serviceKey,
                (s, _) =>
                {
                    var httpClientFactory = s.GetRequiredService<IHttpClientFactory>();
                    var httpClient = httpClientFactory.CreateClient(builder.Name);

                    return factory(httpClient, s);
                }
            );

            return builder;
        }
    }
}

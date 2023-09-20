//
//  SPDX-FileName: VerbTestBase.cs
//  SPDX-FileCopyrightText: Copyright (c) Jarl Gullberg
//  SPDX-License-Identifier: AGPL-3.0-or-later
//

using System;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Remora.Neos.Control.API;
using RichardSzalay.MockHttp;
using Xunit;

#pragma warning disable SA1402

namespace Remora.Neos.Control.Tests.TestBases;

/// <summary>
/// Serves as a base class for REST API tests.
/// </summary>
[Collection("Verb tests")]
public abstract class VerbTestBase
{
    private readonly MockHttpMessageHandler _mockHandler;

    /// <summary>
    /// Gets the services available to the test.
    /// </summary>
    protected IServiceProvider Services { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="VerbTestBase"/> class.
    /// </summary>
    /// <param name="fixture">The test fixture.</param>
    protected VerbTestBase(VerbTestFixture fixture)
    {
        _mockHandler = new();

        var serviceCollection = new ServiceCollection()
            .AddSingleton(fixture);

        Program.ConfigureHeadlessAPIServices(1, "xunit", serviceCollection, b => b.ConfigurePrimaryHttpMessageHandler
        (
            _ => _mockHandler
        ));

        serviceCollection.AddSingleton<IOptionsFactory<JsonSerializerOptions>, OptionsFactory<JsonSerializerOptions>>();
        Decorate<IOptionsFactory<JsonSerializerOptions>, CachedOptionsFactory>(serviceCollection);

        this.Services = serviceCollection.BuildServiceProvider(true);
    }

    /// <summary>
    /// Creates a configured, mocked API instance.
    /// </summary>
    /// <typeparam name="TAPI">The type of the API class to configure and create.</typeparam>
    /// <param name="builder">The mock builder.</param>
    /// <returns>The API instance.</returns>
    protected TAPI ConfigureAPI<TAPI>(Action<MockHttpMessageHandler> builder) where TAPI : AbstractHeadlessRestAPI
    {
        builder(_mockHandler);
        return this.Services.GetRequiredService<TAPI>();
    }

    /// <summary>
    /// Registers a decorator service, replacing the existing interface.
    /// </summary>
    /// <remarks>
    /// Implementation based off of
    /// https://greatrexpectations.com/2018/10/25/decorators-in-net-core-with-dependency-injection/.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <typeparam name="TInterface">The interface type to decorate.</typeparam>
    /// <typeparam name="TDecorator">The decorator type.</typeparam>
    public static void Decorate<TInterface, TDecorator>(IServiceCollection services)
        where TInterface : class
        where TDecorator : class, TInterface
    {
        var wrappedDescriptor = services.First(s => s.ServiceType == typeof(TInterface));

        var objectFactory = ActivatorUtilities.CreateFactory(typeof(TDecorator), new[] { typeof(TInterface) });
        services.Replace(ServiceDescriptor.Describe
        (
            typeof(TInterface),
            s => (TInterface)objectFactory(s, new[] { CreateInstance(s, wrappedDescriptor) }),
            wrappedDescriptor.Lifetime
        ));
    }

    private static object CreateInstance(IServiceProvider services, ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationInstance is not null)
        {
            return descriptor.ImplementationInstance;
        }

        return descriptor.ImplementationFactory is not null
            ? descriptor.ImplementationFactory(services)
            : ActivatorUtilities.GetServiceOrCreateInstance
            (
                services,
                descriptor.ImplementationType ?? throw new InvalidOperationException()
            );
    }

    private class CachedOptionsFactory : IOptionsFactory<JsonSerializerOptions>
    {
        private readonly IOptionsFactory<JsonSerializerOptions> _actual;
        private readonly VerbTestFixture _fixture;

        public CachedOptionsFactory(IOptionsFactory<JsonSerializerOptions> actual, VerbTestFixture fixture)
        {
            _fixture = fixture;
            _actual = actual;
        }

        public JsonSerializerOptions Create(string name)
        {
            return name is "Remora.Neos.Headless"
                ? _fixture.Options
                : _actual.Create(name);
        }
    }
}

/// <summary>
/// Acts as a test fixture for REST API tests.
/// </summary>
public class VerbTestFixture
{
    /// <summary>
    /// Gets a set of JSON serializer options.
    /// </summary>
    public JsonSerializerOptions Options { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="VerbTestFixture"/> class.
    /// </summary>
    public VerbTestFixture()
    {
        var serviceCollection = new ServiceCollection();
        Program.ConfigureJsonSerializerOptions(serviceCollection);

        var services = serviceCollection.BuildServiceProvider();

        this.Options = services.GetRequiredService<IOptionsMonitor<JsonSerializerOptions>>()
            .Get("Remora.Neos.Headless");
    }
}

/// <summary>
/// Defines a test collection for JSON-backed type tests.
/// </summary>
[CollectionDefinition("Verb tests")]
public class VerbTestCollection : ICollectionFixture<VerbTestFixture>
{
}
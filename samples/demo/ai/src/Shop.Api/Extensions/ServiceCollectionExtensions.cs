using Shop.Api.Options;
using Shop.Api.Services;

namespace Shop.Api.Extensions;

/// <summary>
/// Extension methods for registering discount services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers discount-related services and options.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddDiscounts(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<DiscountOptions>(configuration.GetSection(DiscountOptions.SectionName));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IDiscountService, DiscountService>();

        return services;
    }
}

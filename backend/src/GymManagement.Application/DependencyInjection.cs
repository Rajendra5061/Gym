using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace GymManagement.Application;

/// <summary>Registers everything the Application layer owns: currently the FluentValidation validators.</summary>
public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(
            typeof(ApplicationServiceCollectionExtensions).Assembly,
            includeInternalTypes: true);

        return services;
    }
}

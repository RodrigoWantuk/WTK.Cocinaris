using System.Reflection;
using Cocinaris.Api.Inventory;
using Cocinaris.Api.Services;
using Cocinaris.Infrastructure.Persistence;
using Cocinaris.Modules.Inventory.Application;
using Cocinaris.Modules.Inventory.Domain;
using Microsoft.AspNetCore.DataProtection;

namespace Cocinaris.ArchitectureTests;

public sealed class DependencyBoundaryTests
{
    [Fact]
    public void InventoryDomainDoesNotReferenceApiOrEntityFramework()
    {
        var references = typeof(InventoryLot).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("Cocinaris.Api", references);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", references);
        Assert.DoesNotContain("Microsoft.AspNetCore.Http", references);
    }

    [Fact]
    public void ApiDoesNotLeakPersistenceRecordsThroughPublicContracts()
    {
        var contractsAssembly = typeof(Cocinaris.Api.Inventory.CreateLotRequest).Assembly;
        var publicApiTypes = contractsAssembly.GetTypes().Where(type => type.IsPublic && type.Namespace == "Cocinaris.Api.Inventory");

        Assert.DoesNotContain(publicApiTypes, type => type.Name.EndsWith("Record", StringComparison.Ordinal));
    }

    [Fact]
    public void InventoryEndpointsDoNotReceivePersistenceOrDomainObjects()
    {
        var endpointParameters = typeof(InventoryEndpoints)
            .GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.ParameterType);

        Assert.DoesNotContain(typeof(ApplicationDbContext), endpointParameters);
        Assert.DoesNotContain(typeof(InventoryLot), endpointParameters);
        Assert.DoesNotContain(typeof(Product), endpointParameters);
        Assert.DoesNotContain(typeof(InventoryTransaction), endpointParameters);
    }

    [Fact]
    public void InventoryApplicationModuleDoesNotReferenceAspNetCoreOrEntityFramework()
    {
        var references = typeof(InventoryLotApplicationWorkflow).Assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("Microsoft.AspNetCore.Http", references);
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore", references);
        Assert.DoesNotContain("Cocinaris.Api", references);
        Assert.DoesNotContain("Cocinaris.Infrastructure", references);
    }

    [Fact]
    public void InventoryApplicationResultsDoNotExposeHttpStatusOrEtagTokens()
    {
        var applicationTypes = typeof(InventoryLotApplicationWorkflow).Assembly.GetTypes()
            .Where(type => type.Namespace == "Cocinaris.Modules.Inventory.Application");
        var properties = applicationTypes.SelectMany(type => type.GetProperties(BindingFlags.Instance | BindingFlags.Public)).ToList();

        Assert.DoesNotContain(properties, property => property.Name.Equals("StatusCode", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, property => property.Name.Equals("ResponseBody", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, property => property.Name.Contains("Etag", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InventoryApplicationCommandsUseDecodedConcurrencyAndCursorValues()
    {
        var listCursor = typeof(ListInventoryLotsQuery).GetProperty(nameof(ListInventoryLotsQuery.Cursor));
        var updatePrecondition = typeof(UpdateInventoryLotCommand).GetProperty(nameof(UpdateInventoryLotCommand.Precondition));
        var adjustmentPrecondition = typeof(AdjustInventoryLotCommand).GetProperty(nameof(AdjustInventoryLotCommand.Precondition));

        Assert.Equal(typeof(InventoryLotReadCursor), Nullable.GetUnderlyingType(listCursor!.PropertyType) ?? listCursor.PropertyType);
        Assert.Equal(typeof(InventoryVersionPrecondition), updatePrecondition!.PropertyType);
        Assert.Equal(typeof(InventoryVersionPrecondition), adjustmentPrecondition!.PropertyType);
    }

    [Fact]
    public void InventoryModuleDoesNotDeclareATransportTokenAbstraction()
    {
        var types = typeof(InventoryLotApplicationWorkflow).Assembly.GetTypes();

        Assert.DoesNotContain(types, type => type.Name.Contains("TransportToken", StringComparison.Ordinal));
        Assert.DoesNotContain(types, type => type.Name.Contains("DataProtection", StringComparison.Ordinal));
    }

    [Fact]
    public void InventoryVersionEtagsAreStatelessStableAndResourceBound()
    {
        var lotId = Guid.NewGuid();
        var concurrencyToken = Guid.NewGuid();
        var first = new DataProtectionInventoryHttpTokenService(new EphemeralDataProtectionProvider());
        var second = new DataProtectionInventoryHttpTokenService(new EphemeralDataProtectionProvider());

        var emitted = first.WriteVersion(lotId, concurrencyToken);

        Assert.Equal(emitted, second.WriteVersion(lotId, concurrencyToken));
        Assert.True(second.TryReadVersion(lotId, emitted, out var decoded));
        Assert.Equal(concurrencyToken, decoded);
        Assert.False(second.TryReadVersion(Guid.NewGuid(), emitted, out _));
        Assert.DoesNotContain(
            typeof(DataProtectionInventoryHttpTokenService).GetFields(BindingFlags.Instance | BindingFlags.NonPublic),
            field => field.FieldType.Name.Contains("Dictionary", StringComparison.Ordinal));
    }

    [Fact]
    public void InventoryApplicationServiceExposesAllTypedSliceUseCases()
    {
        var methods = typeof(InventoryLotApplicationWorkflow).GetMethods(BindingFlags.Instance | BindingFlags.Public).Select(method => method.Name).ToHashSet(StringComparer.Ordinal);

        Assert.Contains("ListAsync", methods);
        Assert.Contains("GetAsync", methods);
        Assert.Contains("CreateAsync", methods);
        Assert.Contains("UpdateAsync", methods);
        Assert.Contains("AdjustAsync", methods);
        Assert.Contains("DeleteAsync", methods);
        Assert.Contains("HistoryAsync", methods);
    }

    [Fact]
    public void InventoryUseCaseContractsHaveDistinctConcreteHandlers()
    {
        Type[] handlers = [typeof(CreateInventoryLotHandler), typeof(ListInventoryLotsHandler), typeof(GetInventoryLotHandler), typeof(UpdateInventoryLotHandler), typeof(AdjustInventoryLotHandler), typeof(DeleteInventoryLotHandler), typeof(GetInventoryLotHistoryHandler)];

        Assert.Equal(7, handlers.Distinct().Count());
        Assert.All(handlers, handler => Assert.Single(handler.GetInterfaces(), contract => contract.Name.EndsWith("UseCase", StringComparison.Ordinal)));
    }

    [Fact]
    public void ApiAdaptersDoNotInjectPersistenceOrDomainServices()
    {
        var inventoryConstructorParameters = typeof(InventoryApplicationService).GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType);
        var identityConstructorParameters = typeof(CurrentUserService).GetConstructors().Single().GetParameters().Select(parameter => parameter.ParameterType);

        Assert.DoesNotContain(typeof(ApplicationDbContext), inventoryConstructorParameters);
        Assert.DoesNotContain(typeof(InventoryLot), inventoryConstructorParameters);
        Assert.DoesNotContain(typeof(Product), inventoryConstructorParameters);
        Assert.DoesNotContain(typeof(InventoryTransaction), inventoryConstructorParameters);
        Assert.DoesNotContain(typeof(ApplicationDbContext), identityConstructorParameters);
    }
}

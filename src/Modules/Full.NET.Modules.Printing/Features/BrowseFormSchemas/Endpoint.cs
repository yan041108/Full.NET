using Full.NET.Hosting.Api;
using Full.NET.Modules.Identity.Contracts;
using Full.NET.Modules.Printing.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Full.NET.Modules.Printing.Features.BrowseFormSchemas;

internal static class Endpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/printing/form-schemas")
            .WithTags("PrintingFormSchemas");

        group.MapGet("/", (PrintingFormSchemaQueryService queries) =>
            Results.Ok(queries.List()))
        .WithName("printingListFormSchemas")
        .Produces<IReadOnlyList<PrintingFormSchemaDefinition>>(StatusCodes.Status200OK)
        .RequireAuthorization(FullNetPermissionPolicies.For(PrintingFormSchemaPermissions.Read));

        group.MapGet("/{formSchemaKey}", (string formSchemaKey, PrintingFormSchemaQueryService queries) =>
        {
            var schema = queries.TryGet(formSchemaKey);
            return schema is null ? Results.NotFound() : Results.Ok(schema);
        })
        .WithName("printingGetFormSchema")
        .Produces<PrintingFormSchemaDefinition>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound)
        .RequireAuthorization(FullNetPermissionPolicies.For(PrintingFormSchemaPermissions.Read));
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using PulseFlow.Api.Health;

namespace PulseFlow.Api.Http;

public static class PulseFlowHttpApplicationExtensions
{
    public static IServiceCollection AddPulseFlowHttpApplication(this IServiceCollection services)
    {
        services.AddControllers();
        services.AddPulseFlowHealthChecks();
        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();
        services.AddOpenApi(options =>
        {
            options.AddOperationTransformer(
                (operation, context, _) =>
                {
                    if (
                        string.Equals(
                            context.Description.HttpMethod,
                            HttpMethods.Post,
                            StringComparison.OrdinalIgnoreCase
                        )
                        && string.Equals(
                            context.Description.RelativePath,
                            "api/events",
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        operation.RequestBody = new OpenApiRequestBody
                        {
                            Required = false,
                            Content = new Dictionary<string, OpenApiMediaType>
                            {
                                ["application/x-ndjson"] = new()
                                {
                                    Schema = new OpenApiSchema
                                    {
                                        Type = JsonSchemaType.String,
                                        Description = "Raw NDJSON batch accepted for asynchronous processing.",
                                    },
                                },
                            },
                        };
                    }

                    return Task.CompletedTask;
                }
            );
        });

        return services;
    }

    public static WebApplication UsePulseFlowHttpApplication(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();

            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/openapi/v1.json", "PulseFlow v1");
            });
        }

        app.UseHttpsRedirection();
        app.MapPulseFlowHealthChecks();
        app.MapControllers();

        return app;
    }
}

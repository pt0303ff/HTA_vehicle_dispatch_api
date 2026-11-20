
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace vehicle_dispatch_api.OpenApi
{
    // ==========================================
    // Swagger JWT Bearer Security Scheme
    // ==========================================

    internal sealed class BearerSecuritySchemeTransformer
        : IOpenApiDocumentTransformer
    {
        public Task TransformAsync(
            OpenApiDocument document,
            OpenApiDocumentTransformerContext context,
            CancellationToken cancellationToken)
        {
            // 註冊 Bearer Authentication

            document.Components ??= new OpenApiComponents();

            document.Components.SecuritySchemes =
                new Dictionary<string, IOpenApiSecurityScheme>
                {
                    ["Bearer"] = new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.Http,

                        Scheme = "bearer",

                        In = ParameterLocation.Header,

                        BearerFormat = "JWT"
                    }
                };

            return Task.CompletedTask;
        }
    }
}

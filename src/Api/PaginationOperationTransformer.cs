using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using MR.AspNetCore.Pagination;
using MR.AspNetCore.Pagination.Swashbuckle;
using System.ComponentModel;
using VeloTime.Api;

namespace VeloTime.Api
{
    public class PaginationOperationTransformer(IOptions<PaginationOptions> options) : IOpenApiOperationTransformer
    {
        private readonly PaginationOptions _paginationOptions = options.Value;
        private readonly PaginationOperationFilter _filter = new PaginationOperationFilter(options);

        public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
        {
			//var boolSchema = context.SchemaGenerator.GenerateSchema(typeof(bool), context.SchemaRepository);
			//var intSchema = context.SchemaGenerator.GenerateSchema(typeof(int), context.SchemaRepository);

			var boolSchema = new OpenApiSchema { Type = "boolean" };

			try
			{
				if (operation.Responses != null &&
					operation.Responses.Any(s => s.Value != null
												 && s.Value.Content != null
												 && s.Value.Content.Any(c => c.Value != null
																			 && c.Value.Schema != null
																			 && c.Value.Schema.Annotations != null
																			 && c.Value.Schema.Annotations.Any(a => a.Value != null
																													&& a.Value.ToString() != null
																													&& a.Value.ToString()!.Contains("KeysetPagination")))))
				{
					CreateParameter(_paginationOptions.FirstQueryParameterName, "true if you want the first page", boolSchema);
					CreateParameter(_paginationOptions.BeforeQueryParameterName, "Id of the reference entity you want results before");
					CreateParameter(_paginationOptions.AfterQueryParameterName, "Id of the reference entity you want results after");
					CreateParameter(_paginationOptions.LastQueryParameterName, "true if you want the last page", boolSchema);
				}
			} catch (ArgumentNullException e)
			{
				throw;
			}

			//if (PaginationActionDetector.IsKeysetPaginationResultAction(context.MethodInfo, out _))
			//{
			//	CreateParameter(_paginationOptions.FirstQueryParameterName, "true if you want the first page", boolSchema);
			//	CreateParameter(_paginationOptions.BeforeQueryParameterName, "Id of the reference entity you want results before");
			//	CreateParameter(_paginationOptions.AfterQueryParameterName, "Id of the reference entity you want results after");
			//	CreateParameter(_paginationOptions.LastQueryParameterName, "true if you want the last page", boolSchema);
			//}
			//else if (PaginationActionDetector.IsOffsetPaginationResultAction(context.MethodInfo, out _))
			//{
			//	CreateParameter(_paginationOptions.PageQueryParameterName, "The page", intSchema);
			//}

			void CreateParameter(string name, string description, OpenApiSchema? schema = null)
			{
				if (operation.Parameters == null) operation.Parameters = new List<OpenApiParameter>();

				operation.Parameters.Add(new OpenApiParameter
				{
					Required = false,
					In = ParameterLocation.Query,
					Name = name,
					Description = description,
					Schema = schema ?? new OpenApiSchema()
				});
			}

			return Task.CompletedTask;
		}
    }
}


namespace Microsoft.Extensions.DependencyInjection
{
    public static class OpenApiExtensions
    {
        public static void ConfigurePaginationTransform(this OpenApiOptions @this)
        {
            @this.AddOperationTransformer<PaginationOperationTransformer>();
        }
    }
}
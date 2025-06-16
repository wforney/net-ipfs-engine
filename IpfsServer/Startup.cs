using System.IO;
using Ipfs.CoreApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ipfs.Server
{
    /// <summary>
    ///   Startup steps.
    /// </summary>
    class Startup(IConfiguration configuration)
    {
        public IConfiguration Configuration { get; } = configuration;

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<ICoreApi>(Program.IpfsEngine);
            services.AddCors();
            services.AddMvc()
                .AddJsonOptions(jo =>
                {
                    //jo.SerializerSettings.ContractResolver = new DefaultContractResolver()
                    //{
                    //    NamingStrategy = new DefaultNamingStrategy()
                    //};
                });

            // Register the Swagger generator, defining 1 or more Swagger documents
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v0", new Microsoft.OpenApi.Models.OpenApiInfo {
                    Title = "IPFS HTTP API",
                    Description = "The API for interacting with IPFS nodes.",
                    Version = "v0" });

                var path = System.Reflection.Assembly.GetExecutingAssembly().Location;
                path = Path.ChangeExtension(path, ".xml");
                c.IncludeXmlComments(path);
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
            }
            app.UseCors(c => c
                .AllowAnyOrigin() // TODO: This is NOT SAFE
                .AllowAnyHeader()
                .AllowAnyMethod()
                .WithExposedHeaders("X-Stream-Output", "X-Chunked-Output", "X-Content-Length")
            );
            app.UseStaticFiles();

            // Enable middleware to serve generated Swagger as a JSON endpoint.
            app.UseSwagger();

            // Enable middleware to serve swagger-ui (HTML, JS, CSS, etc.),
            // specifying the Swagger JSON endpoint.
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v0/swagger.json", "IPFS HTTP API");
            });

            app.UseMvc();
        }
    }
}

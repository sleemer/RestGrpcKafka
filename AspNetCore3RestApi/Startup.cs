using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DotNetCore3EventLog;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AspNetCore3RestApi
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton(provider => new EventQueue("events"));
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapPost("/events", async context =>
                {
                    var queue = context.RequestServices.GetService<EventQueue>();
                    var cancellationToken = context.RequestAborted;
                    var eventStream = context.Request.Body;

                    var offset = await queue.Append(eventStream, cancellationToken);
                    context.Response.StatusCode = StatusCodes.Status201Created;
                    await context.Response.StartAsync(cancellationToken);
                    await context.Response.WriteAsync($"created at offset: {offset}", cancellationToken);
                });

                endpoints.MapGet("/events/{offset:long=0}", async context =>
                {
                    var queue = context.RequestServices.GetService<EventQueue>();
                    var cancellationToken = context.RequestAborted;
                    var destination = context.Response.Body;
                    var offset = long.Parse((string)context.Request.RouteValues.GetValueOrDefault("offset"));

                    if (!await queue.TryRead(offset, destination, cancellationToken))
                    {
                        context.Response.StatusCode = StatusCodes.Status404NotFound;
                    }
                });

                endpoints.MapGet("/", async context =>
                {
                    await context.Response.WriteAsync("DotNetCore 3 RestApi of AppendOnlyLog");
                });
            });
        }
    }
}

using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using ServiceStack;
using ServiceStack.Text;
using LNDroneController.LND;
using System.Threading;
using System.Linq;
using LNDroneController.Types;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
// using Microsoft.Extensions.Hosting;
// using Microsoft.AspNetCore.Hosting;
// using Serilog;
// using Microsoft.OpenApi.Models;
// using Microsoft.AspNetCore.Builder;

namespace LNDroneController
{
    public class Program
    {
        private static Random r = new Random();
        static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Syntax: LNDroneController <full path to drone config file>");
                return;
            }
            var nodeConnections = new List<LNDNodeConnection>();
            var config = File.ReadAllText(args[0], Encoding.UTF8).FromJson<List<NodeConnectionSettings>>();
            foreach (var node in config)
            {
                var nodeConnection = new LNDNodeConnection();
                nodeConnections.Add(nodeConnection);
                if (!node.LocalIPPath.IsNullOrEmpty())
                    node.LocalIP = node.LocalIPPath.ReadAllText();
                nodeConnection.Start(node.TlsCertFilePath, node.MacaroonFilePath, node.Host, node.LocalIP);
            }
            LNDAutoPaymentEngine.ClusterNodes = nodeConnections;

            var cancellationTokenSources = new List<CancellationTokenSource>();
            var primeSet = new int[] { 53, 59, 61, 67, 71 };

            // foreach (var node in nodeConnections)
            // {
            //     var cs = new CancellationTokenSource();
            //     cancellationTokenSources.Add(cs);
            //     var task = LNDAutoPaymentEngine.Start(node, TimeSpan.FromSeconds(primeSet[r.Next(0, 4)]), token: cs.Token);
            // }


            var cs = new CancellationTokenSource();
            cancellationTokenSources.Add(cs);
            var task = LNDClusterBalancer.Start(nodeConnections,cs.Token);

            // Log.Logger = new LoggerConfiguration()
            //     .WriteTo.Console()
            //     .WriteTo.Seq("http://localhost:5341")
            //     .CreateLogger();


            Console.WriteLine("Press ANY key to stop process");
            Console.ReadKey();
            Console.WriteLine("Sending cancel signals....");
            foreach (var token in cancellationTokenSources)
            {
                token.Cancel();
            }
        }

        // public static IHostBuilder CreateHostBuilder(string[] args) =>
        // Host.CreateDefaultBuilder(args)
        //     .ConfigureWebHostDefaults(webBuilder =>
        //     {
        //         webBuilder.UseStartup<Startup>();
        //     })
        //     .UseSerilog((hostingContext, loggerConfiguration) =>
        // loggerConfiguration.ReadFrom.Configuration(hostingContext.Configuration));
    }


    // public class Startup
    // {
    //     public Startup(IConfiguration configuration)
    //     {
    //         Configuration = configuration;
    //     }

    //     public IConfiguration Configuration { get; }

    //     // This method gets called by the runtime. Use this method to add services to the container.
    //     public void ConfigureServices(IServiceCollection services)
    //     {

    //         services.AddControllers();
    //         services.AddSwaggerGen(c =>
    //         {
    //             c.SwaggerDoc("v1", new OpenApiInfo { Title = "SampleWebAPI50", Version = "v1" });
    //         });
    //     }

    //     // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
    //     public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    //     {
    //         if (env.IsDevelopment())
    //         {
    //             app.UseDeveloperExceptionPage();
    //             app.UseSwagger();
    //             app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "SampleWebAPI50 v1"));
    //         }

    //         app.UseHttpsRedirection();

    //         app.UseRouting();

    //         app.UseAuthorization();

    //         app.UseEndpoints(endpoints =>
    //         {
    //             endpoints.MapControllers();
    //         });
    //     }
    // }

}

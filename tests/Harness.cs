using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;
using Discord;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sanakan.Services.Executor;

namespace Artifacts
{
    public class FakeExecutor : IExecutor
    {
        public bool Accept { get; set; } = true;
        public bool RunImmediately { get; set; }
        public ConcurrentQueue<IExecutable> Added { get; } = new ConcurrentQueue<IExecutable>();

        public Task RunWorker() => Task.CompletedTask;
        public string WhatIsRunning() => "";

        public async Task<bool> TryAdd(IExecutable task, TimeSpan timeout)
        {
            if (!Accept) return false;

            Added.Enqueue(task);
            if (RunImmediately)
                await task.ExecuteAsync(new EmptyServiceProvider());

            return true;
        }
    }

    public static class ControllerHarness
    {
        private static readonly IServiceProvider Services = new ServiceCollection()
            .AddLogging()
            .AddControllers()
            .AddNewtonsoftJson()
            .Services
            .BuildServiceProvider();

        public static T Attach<T>(T controller) where T : ControllerBase
        {
            var ctx = new DefaultHttpContext { RequestServices = Services };
            ctx.Response.Body = new MemoryStream();
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = ctx,
                RouteData = new RouteData(),
                ActionDescriptor = new ControllerActionDescriptor(),
            };
            return controller;
        }

        // kontrolery zwracają wynik zamiast pisać odpowiedź same, więc sprawdzamy zwrócony obiekt
        public static int? StatusOf(this IActionResult result) => result switch
        {
            ObjectResult obj => obj.StatusCode,
            StatusCodeResult code => code.StatusCode,
            _ => null,
        };

        public static int? StatusOf<T>(this ActionResult<T> result) => result.Result.StatusOf();

        public static string MessageOf(this IActionResult result)
            => (string)Newtonsoft.Json.Linq.JObject.FromObject((result as ObjectResult).Value)["message"];
    }

    public static class Users
    {
        public static IUser Discord(ulong id)
        {
            var mock = new Mock<IUser>();
            mock.SetupGet(x => x.Id).Returns(id);
            mock.SetupGet(x => x.Mention).Returns($"<@{id}>");
            return mock.Object;
        }
    }
}

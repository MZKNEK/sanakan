#pragma warning disable 1591

using System;
using System.Threading.Tasks;

namespace Sanakan.Services.Executor
{
    public interface IExecutor
    {
        Task RunWorker();
        string WhatIsRunning();
        Task<bool> TryAdd(IExecutable task, TimeSpan timeout);
    }
}

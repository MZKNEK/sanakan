#pragma warning disable 1591

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Sanakan.Services.Executor
{
    public class Executable : IExecutable
    {
        private Func<Task> _task { get; set; }
        private Task _internalTask { get; set; }
        private readonly TaskCompletionSource _started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly string _name;
        private readonly Priority _priority;
        private readonly List<ulong> _owners;

        public Executable(string name, Func<Task> task, ulong owner = 0, Priority priority = Priority.Normal)
        {
            _name = name;
            _task = task;
            _internalTask = null;
            _priority = priority;
            _owners =  new List<ulong>() {owner};
        }

        public Executable(string name, Task task, ulong owner = 0, Priority priority = Priority.Normal)
        {
            _name = name;
            _task = null;
            _internalTask = task;
            _priority = priority;
            _owners =  new List<ulong>() {owner};
        }

        public void AddOwner(ulong id) => _owners.Add(id);

        public IEnumerable<ulong> GetOwners() => _owners;

        public Priority GetPriority() => _priority;

        public string GetName() => _name;

        public void Wait() => WaitAsync().GetAwaiter().GetResult();

        public async Task WaitAsync()
        {
            await _started.Task.ConfigureAwait(false);
            await _internalTask.ConfigureAwait(false);
        }

        public async Task<bool> ExecuteAsync(IServiceProvider provider)
        {
            try
            {
                try
                {
                    if (_internalTask is null)
                    {
                        _internalTask = _task();
                    }
                    else
                    {
                        _internalTask.Start();
                    }
                }
                catch (Exception ex) when (_internalTask is null)
                {
                    _internalTask = Task.FromException(ex);
                }
                finally
                {
                    _started.TrySetResult();
                }

                await _internalTask.ConfigureAwait(false);

                if (_internalTask is Task<bool> bTask)
                {
                    return bTask.GetAwaiter().GetResult();
                }

                return true;
            }
            catch (Exception ex)
            {
                throw new Exception("in executable:", ex);
            }
        }
    }
}

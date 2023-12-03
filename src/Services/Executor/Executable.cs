#pragma warning disable 1591

using System;
using System.Threading.Tasks;

namespace Sanakan.Services.Executor
{
    public class Executable : IExecutable
    {
        private Func<Task> _task { get; set; }
        private Task _internalTask { get; set; }

        private readonly string _name;
        private readonly Priority _priority;

        public Executable(string name, Func<Task> task, Priority priority = Priority.Normal)
        {
            _name = name;
            _task = task;
            _internalTask = null;
            _priority = priority;
        }

        public Executable(string name, Task task, Priority priority = Priority.Normal)
        {
            _name = name;
            _task = null;
            _internalTask = task;
            _priority = priority;
        }

        public Priority GetPriority() => _priority;

        public string GetName() => _name;

        public void Wait()
        {
            while (_internalTask is null) {}
            _internalTask.Wait();
        }

        public async Task<bool> ExecuteAsync(IServiceProvider provider)
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

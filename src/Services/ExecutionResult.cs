#pragma warning disable 1591

namespace Sanakan.Services
{
    public class ExecutionResult
    {
        public enum EStatus
        {
            Ok, Error
        }

        public EStatus Status { get; set; }
        public string Message { get; set; }

        static public ExecutionResult FromError(string msg)
            => new ExecutionResult { Status = EStatus.Error, Message = msg };

        static public ExecutionResult FromSucces(string msg)
            => new ExecutionResult { Status = EStatus.Ok, Message = msg };

        public bool IsOk() => Status == EStatus.Ok;
        public bool IsError() => Status == EStatus.Error;
    }
}

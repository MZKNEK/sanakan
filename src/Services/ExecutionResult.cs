#pragma warning disable 1591

using Discord;
using Sanakan.Extensions;

namespace Sanakan.Services
{
    public class ExecutionResult
    {
        public enum EStatus
        {
            Ok, Error
        }

        public EStatus Status { get; private set; }
        public string Message { get; private set; }
        public EMType MessageType { get; private set; }

        static public ExecutionResult FromError(string msg, EMType type = EMType.Error) => new ExecutionResult
        {
            Message = msg,
            MessageType = type,
            Status = EStatus.Error,
        };

        static public ExecutionResult FromSuccess(string msg, EMType type = EMType.Success) => new ExecutionResult
        {
            Message = msg,
            MessageType = type,
            Status = EStatus.Ok,
        };

        public bool IsOk() => Status == EStatus.Ok;
        public bool IsError() => Status == EStatus.Error;

        public EmbedBuilder ToEmbedMessage(string prefix = "", string suffix = "") => $"{prefix}{Message}{suffix}".ToEmbedMessage(MessageType);
    }
}

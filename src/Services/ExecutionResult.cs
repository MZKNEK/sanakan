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
        public UserActivityBuilder Activity { get; private set; }

        static public ExecutionResult FromError(string msg, EMType type = EMType.Error) => new ExecutionResult
        {
            Message = msg,
            MessageType = type,
            Status = EStatus.Error,
            Activity = null,
        };

        static public ExecutionResult FromSuccessWithActivity(string msg, UserActivityBuilder builder, EMType type = EMType.Success) => new ExecutionResult
        {
            Message = msg,
            MessageType = type,
            Status = EStatus.Ok,
            Activity = builder,
        };

        static public ExecutionResult FromSuccess(string msg, EMType type = EMType.Success) => new ExecutionResult
        {
            Message = msg,
            MessageType = type,
            Status = EStatus.Ok,
            Activity = null,
        };

        static public ExecutionResult From(ImageUrlCheckResult res)
        {
            var result = new ExecutionResult
            {
                MessageType = EMType.Error,
                Status = EStatus.Error
            };

            switch(res)
            {
                case ImageUrlCheckResult.Ok:
                {
                    result.Status = EStatus.Ok;
                    result.MessageType = EMType.Success;
                    result.Message = "Adres jest poprawny!";
                }
                break;

                case ImageUrlCheckResult.WrongExtension:
                    result.Message = "Nie wykryto obrazka! Adres ma niedozwolone rozszerzenie!";
                break;

                case ImageUrlCheckResult.BlacklistedHost:
                    result.Message = "Host podanego adresu nie znajduje się na białej liście!";
                break;

                case ImageUrlCheckResult.TransformError:
                    result.Message = "Nie udało się zamienić adresu znanego hosta na poprawy!";
                break;

                default:
                case ImageUrlCheckResult.NotUrl:
                    result.Message = "Nie wykryto obrazka! Upewnij się, że podałeś poprawny adres!";
                break;
            }

            return result;
        }

        public bool IsOk() => Status == EStatus.Ok;
        public bool IsError() => Status == EStatus.Error;
        public bool IsActivity() => Activity != null;

        public EmbedBuilder ToEmbedMessage(string prefix = "", string suffix = "") => $"{prefix}{Message}{suffix}".ToEmbedMessage(MessageType);

        public static implicit operator bool(ExecutionResult result) => result?.IsOk() ?? false;
    }
}

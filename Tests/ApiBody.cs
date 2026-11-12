using Microsoft.AspNetCore.Mvc;

namespace Artifacts
{
    // reads the fields of an anonymous response body
    public static class ApiBody
    {
        public static object Field(IActionResult result, string name)
        {
            var value = ((ObjectResult)result).Value;
            return value.GetType().GetProperty(name)?.GetValue(value);
        }

        public static string Code(IActionResult result) => (string)Field(result, "code");

        public static object Detail(IActionResult result, string name)
        {
            var details = Field(result, "details");
            return details?.GetType().GetProperty(name)?.GetValue(details);
        }

        public static string Message(IActionResult result) => (string)Field(result, "message");
    }
}

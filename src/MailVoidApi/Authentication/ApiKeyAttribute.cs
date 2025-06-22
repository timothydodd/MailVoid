using Microsoft.AspNetCore.Authorization;

namespace MailVoidApi.Authentication;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class ApiKeyAttribute : AuthorizeAttribute
{
    public ApiKeyAttribute() : base("ApiKey")
    {
    }

    public ApiKeyAttribute(params string[] scopes) : base("ApiKey")
    {
        if (scopes.Length > 0)
        {
            Policy = $"ApiKey.{string.Join(",", scopes)}";
        }
    }
}
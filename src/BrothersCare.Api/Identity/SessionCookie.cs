namespace BrothersCare.Api.Identity;

public sealed class SessionCookie
{
    private readonly bool secure;

    public SessionCookie(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        Name = configuration["Authentication:CookieName"] ?? "bc_session";
        if (string.IsNullOrWhiteSpace(Name) || !Name.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
        {
            throw new InvalidOperationException("Authentication:CookieName must be a valid cookie name.");
        }

        secure = configuration.GetValue("Authentication:CookieSecure", true);
        if (!secure && !environment.IsDevelopment())
        {
            throw new InvalidOperationException("Session cookies must be secure outside Development.");
        }
    }

    public string Name { get; }

    public string? Read(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Request.Cookies[Name];
    }

    public void Write(HttpContext context, string value, DateTimeOffset absoluteExpiresAt)
    {
        ArgumentNullException.ThrowIfNull(context);
        var options = CreateOptions();
        options.Expires = absoluteExpiresAt;
        context.Response.Cookies.Append(Name, value, options);
    }

    public void Clear(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Response.Cookies.Delete(Name, CreateOptions());
    }

    private CookieOptions CreateOptions() => new()
    {
        HttpOnly = true,
        Secure = secure,
        SameSite = SameSiteMode.Lax,
        Path = "/api",
        IsEssential = true,
    };
}

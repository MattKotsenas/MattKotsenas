namespace MattKotsenas.AppHost;

internal static class BlogDomains
{
    public const string Root = "kotsenas.com";
    public const string Blog = "matt.kotsenas.com";

    public static IEnumerable<string> PublicHostnames =>
    [
        Root,
        $"www.{Root}",
        Blog,
        $"www.{Blog}",
    ];
}

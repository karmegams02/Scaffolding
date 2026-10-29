New and improved scaffolding experience. 
More details coming soon!

## Blazor CRUD routing

On .NET 9 and later, generated EF Core CRUD pages use static server-side rendering. For apps with global interactivity, CRUD scaffolding configures both `Routes` and `HeadOutlet` to use `HttpContext.AcceptsInteractiveRouting()`, retaining the selected interactive render mode for other pages and using `null` for excluded endpoints. Existing endpoint-aware Identity routing is preserved.
using Host;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

var builder = WebApplication
    .CreateBuilder(args);

builder
    .Services
    .AddScoped<SimulatedTransientErrorInterceptor>()
    .AddScoped<SimulatedConnectionFaultInterceptor>()
    .AddDbContext<FooContext>((sp, ef) =>
    {
        const string ConnectionStringName = "Foo";

        var connectionString = builder
            .Configuration
            .GetConnectionString(ConnectionStringName);

        IInterceptor[] interceptors =
        [
            sp.GetRequiredService<SimulatedTransientErrorInterceptor>(),
            sp.GetRequiredService<SimulatedConnectionFaultInterceptor>(),
        ];

        ef.UseNpgsql(
            connectionString,
            pg =>
            {
                pg.EnableRetryOnFailure(
                    maxRetryCount: 10,
                    maxRetryDelay: TimeSpan.FromSeconds(30),
                    errorCodesToAdd: null);
            });
        
        ef.AddInterceptors(interceptors);
    });

builder
    .Services
    .AddHealthChecks()
    .AddDbContextCheck<FooContext>(
        customTestQuery: (context, token) => 
            context.Database.CanConnectAsync(token))
    .AddCheck<FooHealthCheck>("foo");

var app = builder
    .Build();

app.MapHealthChecks("/health"); 

app.MapGet(
    "/", 
    () => 
        "Hello World!");

app.MapGet(
    "/items", 
    async (FooContext db) => 
        await db.Items.ToListAsync());

app.MapPost(
    "/items",
    async (FooContext db, Item item) =>
    {
        item.Id = Guid.NewGuid();
        await db.Items.AddAsync(item);
        await db.SaveChangesAsync();
        return Results.Created($"/items/{item.Id}", item);
    });

app.Run();

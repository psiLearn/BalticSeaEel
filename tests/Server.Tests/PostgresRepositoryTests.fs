module PostgresRepositoryTests

open System
open System.Threading.Tasks
open System
open Npgsql
open Testcontainers.PostgreSql
open Xunit
open Xunit.Sdk
open Eel.Server.Services
open Shared

type PostgresFactAttribute() =
    inherit FactAttribute()

    do
        let skipEnv = Environment.GetEnvironmentVariable("EEL_SKIP_PG_TESTS")
        if String.Equals(skipEnv, "1", StringComparison.OrdinalIgnoreCase) then
            base.Skip <- "Postgres tests disabled via EEL_SKIP_PG_TESTS=1"

type PostgresFixture() =
    let mutable available = false
    let mutable connectionString = ""
    let mutable reason = "Docker/Testcontainers unavailable."

    let container =
        PostgreSqlBuilder()
            .WithImage("postgres:15")
            .WithDatabase("eel_test")
            .WithUsername("eel")
            .WithPassword("eel")
            .WithCleanUp(true)
            .Build()

    interface IAsyncLifetime with
        member _.InitializeAsync() =
            task {
                let skipEnv = Environment.GetEnvironmentVariable("EEL_SKIP_PG_TESTS")
                if String.Equals(skipEnv, "1", StringComparison.OrdinalIgnoreCase) then
                    available <- false
                    reason <- "EEL_SKIP_PG_TESTS=1"
                else
                    try
                        do! container.StartAsync()
                        connectionString <- container.GetConnectionString()
                        available <- true
                        reason <- ""
                        // prime schema
                        PostgresScoreRepository(connectionString) |> ignore
                        do! PostgresFixture.ClearScores(connectionString)
                    with ex ->
                        available <- false
                        reason <- $"Docker unavailable ({ex.Message})"
            }

        member _.DisposeAsync() =
            container.DisposeAsync().AsTask()

    member _.ConnectionString =
        if not available then invalidOp "Postgres container not available."
        else connectionString

    member _.IsAvailable = available
    member _.SkipReason = reason

    static member ClearScores(connectionString: string) =
        task {
            use conn = new NpgsqlConnection(connectionString)
            do! conn.OpenAsync()
            use cmd = conn.CreateCommand()
            cmd.CommandText <- "DELETE FROM scores;"
            let! _ = cmd.ExecuteNonQueryAsync()
            return ()
        }

[<CollectionDefinition("postgres")>]
type PostgresCollection() =
    interface ICollectionFixture<PostgresFixture>

[<Collection("postgres")>]
type ``Postgres repository``(fixture: PostgresFixture) =

    let repo () = PostgresScoreRepository(fixture.ConnectionString) :> IScoreRepository

    let reset () =
        PostgresFixture.ClearScores(fixture.ConnectionString).GetAwaiter().GetResult()

    let ensureAvailable () =
        if not fixture.IsAvailable then
            let reason = fixture.SkipReason
            raise (XunitException($"Postgres tests unavailable: {reason}. Set EEL_SKIP_PG_TESTS=1 to skip."))

    [<PostgresFact>]
    member _.``UpsertScore persists max per player`` () =
        ensureAvailable ()
        reset ()
        let repository = repo ()
        let inserted = repository.UpsertScore { Name = "Ina"; Score = 10 }
        Assert.Equal(10, inserted.Score)

        let lower = repository.UpsertScore { Name = "Ina"; Score = 5 }
        Assert.Equal(10, lower.Score)

        let higher = repository.UpsertScore { Name = "Ina"; Score = 42 }
        Assert.Equal(42, higher.Score)

        let stored = repository.GetTopScore() |> Option.get
        Assert.Equal(42, stored.Score)
        Assert.Equal("Ina", stored.Name)

    [<PostgresFact>]
    member _.``GetScores returns ordered top ten`` () =
        ensureAvailable ()
        reset ()
        let repository = repo ()
        for i in 1 .. 12 do
            let name = $"Player{i}"
            repository.UpsertScore { Name = name; Score = i * 3 } |> ignore

        let scores = repository.GetScores()
        Assert.Equal(10, scores.Length)
        let top = scores |> List.head
        Assert.Equal("Player12", top.Name)
        let actual = scores |> List.map (fun s -> s.Score)
        let expected = actual |> List.sortDescending
        Assert.Equal<int list>(expected, actual)

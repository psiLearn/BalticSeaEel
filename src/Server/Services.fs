namespace Eel.Server.Services

open System
open System.Collections.Generic
open System.Data.Common
open System.Threading
open Npgsql
open Shared

type IScoreRepository =
    abstract member GetScores: unit -> HighScore list
    abstract member GetTopScore: unit -> HighScore option
    abstract member UpsertScore: HighScore -> HighScore

type InMemoryScoreRepository() =
    let mutable storage: Map<string, int> = Map.empty
    let syncRoot = obj()

    let ordered () =
        storage
        |> Seq.map (fun kv -> { Name = kv.Key; Score = kv.Value })
        |> Seq.sortByDescending (fun score -> score.Score)
        |> Seq.truncate 10
        |> Seq.toList

    interface IScoreRepository with
        member _.GetScores() =
            lock syncRoot ordered

        member _.GetTopScore() =
            lock syncRoot (fun () -> ordered () |> List.tryHead)

        member _.UpsertScore score =
            lock syncRoot (fun () ->
                let current =
                    storage
                    |> Map.tryFind score.Name
                    |> Option.defaultValue 0

                let value = max current score.Score
                storage <- storage |> Map.add score.Name value
                { Name = score.Name; Score = value })

type PostgresScoreRepository(connectionString: string) =
    let ensureSchema () =
        use conn = new NpgsqlConnection(connectionString)
        conn.Open()
        use cmd = conn.CreateCommand()
        cmd.CommandText <- """
            CREATE TABLE IF NOT EXISTS scores (
                name TEXT PRIMARY KEY,
                score INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS vocabulary (
                id SERIAL PRIMARY KEY,
                topic TEXT NOT NULL,
                language1 TEXT NOT NULL,
                language2 TEXT NOT NULL,
                example TEXT NOT NULL
            );
        """
        cmd.ExecuteNonQuery() |> ignore

        use seedScore = conn.CreateCommand()
        seedScore.CommandText <- "INSERT INTO scores(name, score) VALUES ('Anonymous', 0) ON CONFLICT (name) DO NOTHING;"
        seedScore.ExecuteNonQuery() |> ignore

    do ensureSchema ()

    let readScores (reader: DbDataReader) =
        let name = reader.GetString(0)
        let score = reader.GetInt32(1)
        { Name = name; Score = score }

    interface IScoreRepository with
        member _.GetScores() =
            use conn = new NpgsqlConnection(connectionString)
            conn.Open()
            use cmd = conn.CreateCommand()
            cmd.CommandText <- "SELECT name, score FROM scores ORDER BY score DESC LIMIT 10;"
            use reader = cmd.ExecuteReader()
            let scores = ResizeArray()
            while reader.Read() do
                scores.Add(readScores reader)
            scores |> Seq.toList

        member _.GetTopScore() =
            use conn = new NpgsqlConnection(connectionString)
            conn.Open()
            use cmd = conn.CreateCommand()
            cmd.CommandText <- "SELECT name, score FROM scores ORDER BY score DESC LIMIT 1;"
            use reader = cmd.ExecuteReader()
            if reader.Read() then
                Some (readScores reader)
            else
                None

        member _.UpsertScore score =
            use conn = new NpgsqlConnection(connectionString)
            conn.Open()
            use cmd = conn.CreateCommand()
            cmd.CommandText <- """
                INSERT INTO scores(name, score)
                VALUES (@name, @score)
                ON CONFLICT (name)
                DO UPDATE SET score = GREATEST(scores.score, EXCLUDED.score)
                RETURNING name, score;
            """
            cmd.Parameters.AddWithValue("@name", score.Name) |> ignore
            cmd.Parameters.AddWithValue("@score", score.Score) |> ignore
            use reader = cmd.ExecuteReader()
            if reader.Read() then
                readScores reader
            else
                score

type HighScoreStore(repository: IScoreRepository) =
    let sanitizeName (name: string) =
        let trimmed =
            if obj.ReferenceEquals(name, null) then
                ""
            else
                name.Trim()

        if String.IsNullOrWhiteSpace trimmed then "Anonymous" else trimmed

    let sanitizeScore score = max 0 score

    let defaultScore = { Name = "Anonymous"; Score = 0 }

    member _.Get() = repository.GetTopScore() |> Option.defaultValue defaultScore

    member _.GetAll() =
        match repository.GetScores() with
        | [] -> [ defaultScore ]
        | scores -> scores

    member _.Upsert candidate =
        let sanitized =
            { Name = sanitizeName candidate.Name
              Score = sanitizeScore candidate.Score }

        repository.UpsertScore sanitized

module Vocabulary =
    let entries: VocabularyEntry list =
        [ { Topic = "Maison"
            Language1 = "la maison"
            Language2 = "das Haus"
            Example = "Ma maison est grande et blanche." }
          { Topic = "Maison"
            Language1 = "la chambre"
            Language2 = "das Schlafzimmer"
            Example = "Je dors dans ma chambre." }
          { Topic = "Maison"
            Language1 = "la cuisine"
            Language2 = "die Küche"
            Example = "Nous cuisinons dans la cuisine." }
          { Topic = "Maison"
            Language1 = "la porte"
            Language2 = "die Tür"
            Example = "Ferme la porte, s'il te plaît !" }
          { Topic = "Maison"
            Language1 = "la fenêtre"
            Language2 = "das Fenster"
            Example = "J'ouvre la fenêtre le matin." }
          { Topic = "Famille"
            Language1 = "le père"
            Language2 = "der Vater"
            Example = "Mon père travaille à l'hôpital." }
          { Topic = "Famille"
            Language1 = "la mère"
            Language2 = "die Mutter"
            Example = "Ma mère prépare le dîner." }
          { Topic = "Famille"
            Language1 = "le frère"
            Language2 = "der Bruder"
            Example = "J'ai un frère plus jeune." }
          { Topic = "Famille"
            Language1 = "la soeur"
            Language2 = "die Schwester"
            Example = "Ma soeur aime chanter." }
          { Topic = "Famille"
            Language1 = "les parents"
            Language2 = "die Eltern"
            Example = "Mes parents sont gentils." }
          { Topic = "École"
            Language1 = "l'école"
            Language2 = "die Schule"
            Example = "L'école commence à huit heures." }
          { Topic = "École"
            Language1 = "le professeur"
            Language2 = "der Lehrer / die Lehrerin"
            Example = "Le professeur est sympathique." }
          { Topic = "École"
            Language1 = "l'élève"
            Language2 = "der Schüler / die Schülerin"
            Example = "L'élève écrit au tableau." }
          { Topic = "École"
            Language1 = "le cahier"
            Language2 = "das Heft"
            Example = "J'écris dans mon cahier." }
          { Topic = "École"
            Language1 = "le stylo"
            Language2 = "der Stift"
            Example = "J'ai perdu mon stylo." }
          { Topic = "Nourriture"
            Language1 = "le pain"
            Language2 = "das Brot"
            Example = "Je mange du pain le matin." }
          { Topic = "Nourriture"
            Language1 = "le fromage"
            Language2 = "der Käse"
            Example = "Le fromage français est délicieux." }
          { Topic = "Nourriture"
            Language1 = "la pomme"
            Language2 = "der Apfel"
            Example = "Elle mange une pomme rouge." }
          { Topic = "Nourriture"
            Language1 = "le lait"
            Language2 = "die Milch"
            Example = "Il boit du lait." }
          { Topic = "Nourriture"
            Language1 = "l'eau"
            Language2 = "das Wasser"
            Example = "Je bois de l'eau tous les jours." }
          { Topic = "Animaux"
            Language1 = "le chien"
            Language2 = "der Hund"
            Example = "Mon chien s'appelle Max." }
          { Topic = "Animaux"
            Language1 = "le chat"
            Language2 = "die Katze"
            Example = "Le chat dort sur le lit." }
          { Topic = "Animaux"
            Language1 = "le cheval"
            Language2 = "das Pferd"
            Example = "Ma cousine monte à cheval." }
          { Topic = "Animaux"
            Language1 = "l'oiseau"
            Language2 = "der Vogel"
            Example = "L'oiseau chante dans le jardin." }
          { Topic = "Temps"
            Language1 = "le soleil"
            Language2 = "die Sonne"
            Example = "Le soleil brille aujourd'hui." }
          { Topic = "Temps"
            Language1 = "la pluie"
            Language2 = "der Regen"
            Example = "Il pleut beaucoup en automne." }
          { Topic = "Temps"
            Language1 = "la neige"
            Language2 = "der Schnee"
            Example = "Les enfants jouent dans la neige." }
          { Topic = "Temps"
            Language1 = "chaud"
            Language2 = "warm"
            Example = "Il fait chaud en été." }
          { Topic = "Temps"
            Language1 = "froid"
            Language2 = "kalt"
            Example = "En hiver, il fait très froid." }
          { Topic = "Verbes"
            Language1 = "être"
            Language2 = "sein"
            Example = "Je suis content." }
          { Topic = "Verbes"
            Language1 = "avoir"
            Language2 = "haben"
            Example = "Nous avons un chat." }
          { Topic = "Verbes"
            Language1 = "aller"
            Language2 = "gehen"
            Example = "Je vais à l'école." }
          { Topic = "Verbes"
            Language1 = "faire"
            Language2 = "machen"
            Example = "Elle fait du sport." }
          { Topic = "Verbes"
            Language1 = "aimer"
            Language2 = "mögen / lieben"
            Example = "J'aime la musique." }
          { Topic = "Verbes"
            Language1 = "parler"
            Language2 = "sprechen"
            Example = "Ils parlent français." }
          { Topic = "Quotidien"
            Language1 = "le matin"
            Language2 = "der Morgen"
            Example = "Je me lève tôt le matin." }
          { Topic = "Quotidien"
            Language1 = "le soir"
            Language2 = "der Abend"
            Example = "Le soir, je regarde un film." }
          { Topic = "Quotidien"
            Language1 = "aujourd'hui"
            Language2 = "heute"
            Example = "Aujourd'hui, il fait beau." }
          { Topic = "Quotidien"
            Language1 = "demain"
            Language2 = "morgen"
            Example = "Demain, nous allons au cinéma." }
          { Topic = "Quotidien"
            Language1 = "souvent"
            Language2 = "oft"
            Example = "Je vais souvent au parc." } ]

    let private rng = Random()

    let getRandom () =
        entries[rng.Next(entries.Length)]

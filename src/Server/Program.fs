module Eel.Server.Program

open System
open System.Threading.Tasks
open System.Collections.Generic
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.OpenApi.Models
open Swashbuckle.AspNetCore.SwaggerGen
open Giraffe
open Shared

type HighScoreStore() =
    let mutable highScore = { Name = "Anonymous"; Score = 0 }

    member _.Get() = highScore

    member _.Upsert candidate =
        if candidate.Score > highScore.Score then
            highScore <- candidate

        highScore

let vocabularyEntries: VocabularyEntry list =
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
        Example = "Ferme la porte, s’il te plaît !" }
      { Topic = "Maison"
        Language1 = "la fenêtre"
        Language2 = "das Fenster"
        Example = "J’ouvre la fenêtre le matin." }
      { Topic = "Famille"
        Language1 = "le père"
        Language2 = "der Vater"
        Example = "Mon père travaille à l’hôpital." }
      { Topic = "Famille"
        Language1 = "la mère"
        Language2 = "die Mutter"
        Example = "Ma mère prépare le dîner." }
      { Topic = "Famille"
        Language1 = "le frère"
        Language2 = "der Bruder"
        Example = "J’ai un frère plus jeune." }
      { Topic = "Famille"
        Language1 = "la sœur"
        Language2 = "die Schwester"
        Example = "Ma sœur aime chanter." }
      { Topic = "Famille"
        Language1 = "les parents"
        Language2 = "die Eltern"
        Example = "Mes parents sont gentils." }
      { Topic = "École"
        Language1 = "l’école"
        Language2 = "die Schule"
        Example = "L’école commence à huit heures." }
      { Topic = "École"
        Language1 = "le professeur"
        Language2 = "der Lehrer / die Lehrerin"
        Example = "Le professeur est sympathique." }
      { Topic = "École"
        Language1 = "l’élève"
        Language2 = "der Schüler / die Schülerin"
        Example = "L’élève écrit au tableau." }
      { Topic = "École"
        Language1 = "le cahier"
        Language2 = "das Heft"
        Example = "J’écris dans mon cahier." }
      { Topic = "École"
        Language1 = "le stylo"
        Language2 = "der Stift"
        Example = "J’ai perdu mon stylo." }
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
        Language1 = "l’eau"
        Language2 = "das Wasser"
        Example = "Je bois de l’eau tous les jours." }
      { Topic = "Animaux"
        Language1 = "le chien"
        Language2 = "der Hund"
        Example = "Mon chien s’appelle Max." }
      { Topic = "Animaux"
        Language1 = "le chat"
        Language2 = "die Katze"
        Example = "Le chat dort sur le lit." }
      { Topic = "Animaux"
        Language1 = "le cheval"
        Language2 = "das Pferd"
        Example = "Ma cousine monte à cheval." }
      { Topic = "Animaux"
        Language1 = "l’oiseau"
        Language2 = "der Vogel"
        Example = "L’oiseau chante dans le jardin." }
      { Topic = "Temps"
        Language1 = "le soleil"
        Language2 = "die Sonne"
        Example = "Le soleil brille aujourd’hui." }
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
        Example = "Je vais à l’école." }
      { Topic = "Verbes"
        Language1 = "faire"
        Language2 = "machen"
        Example = "Elle fait du sport." }
      { Topic = "Verbes"
        Language1 = "aimer"
        Language2 = "mögen / lieben"
        Example = "J’aime la musique." }
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
        Language1 = "aujourd’hui"
        Language2 = "heute"
        Example = "Aujourd’hui, il fait beau." }
      { Topic = "Quotidien"
        Language1 = "demain"
        Language2 = "morgen"
        Example = "Demain, nous allons au cinéma." }
      { Topic = "Quotidien"
        Language1 = "souvent"
        Language2 = "oft"
        Example = "Je vais souvent au parc." } ]

let vocabularyRng = Random()

let getRandomVocabulary () =
    vocabularyEntries[vocabularyRng.Next(vocabularyEntries.Length)]

let getHighScoreHandler: HttpHandler =
    fun next ctx ->
        let store = ctx.GetService<HighScoreStore>()
        json (store.Get()) next ctx

let saveHighScoreHandler: HttpHandler =
    fun next ctx ->
        task {
            let store = ctx.GetService<HighScoreStore>()
            let! candidate = ctx.BindJsonAsync<HighScore>()
            let updated = store.Upsert candidate
            return! json updated next ctx
        }

let getVocabularyHandler: HttpHandler =
    fun next ctx -> json (getRandomVocabulary ()) next ctx

let apiRoutes =
    choose [ GET >=> route "/highscore" >=> getHighScoreHandler
             POST
             >=> route "/highscore"
             >=> saveHighScoreHandler
             GET
             >=> route "/vocabulary"
             >=> getVocabularyHandler ]

type ApiDocumentFilter() =
    interface IDocumentFilter with
        member _.Apply(doc, _context) =
            if isNull doc.Components then
                doc.Components <- OpenApiComponents()

            if isNull doc.Paths then
                doc.Paths <- OpenApiPaths()

            let schemas = doc.Components.Schemas

            let ensureSchema (id: string) (schemaFactory: unit -> OpenApiSchema) =
                if schemas.ContainsKey(id) |> not then
                    schemas.Add(id, schemaFactory())

            ensureSchema "HighScore" (fun _ ->
                OpenApiSchema(
                    Type = "object",
                    Properties =
                        dict [ "name", OpenApiSchema(Type = "string")
                               "score", OpenApiSchema(Type = "integer", Format = "int32") ],
                    Required = HashSet<string>([| "name"; "score" |])
                ))

            ensureSchema "VocabularyEntry" (fun _ ->
                OpenApiSchema(
                    Type = "object",
                    Properties =
                        dict [ "topic", OpenApiSchema(Type = "string")
                               "language1", OpenApiSchema(Type = "string")
                               "language2", OpenApiSchema(Type = "string")
                               "example", OpenApiSchema(Type = "string") ],
                    Required = HashSet<string>([| "topic"; "language1"; "language2"; "example" |])
                ))

            let refSchema name =
                OpenApiSchema(Reference = OpenApiReference(Type = ReferenceType.Schema, Id = name))

            let jsonContent schemaRef =
                let media = OpenApiMediaType()
                media.Schema <- schemaRef
                media

            let ensureResponse (schemaName: string) (description: string) =
                let content = Dictionary<string, OpenApiMediaType>()
                content.Add("application/json", jsonContent (refSchema schemaName))
                let resp = OpenApiResponse(Description = description)
                resp.Content <- content
                resp

            let singleTag name =
                ResizeArray([| OpenApiTag(Name = name) |]) :> IList<OpenApiTag>

            let ensureOperation (item: OpenApiPathItem) opType (factory: unit -> OpenApiOperation) =
                if isNull item.Operations then
                    item.Operations <- Dictionary<OperationType, OpenApiOperation>()

                if item.Operations.ContainsKey(opType) |> not then
                    item.Operations.Add(opType, factory())

            let ensurePath (path: string) (configure: OpenApiPathItem -> unit) =
                if doc.Paths.ContainsKey(path) then
                    configure doc.Paths[path]
                else
                    let item = OpenApiPathItem()
                    configure item
                    doc.Paths.Add(path, item)

            ensurePath "/api/highscore" (fun item ->
                ensureOperation item OperationType.Get (fun _ ->
                    let op = OpenApiOperation(Summary = "Gets the current high score.")
                    op.Responses <- OpenApiResponses()
                    op.Responses.Add("200", ensureResponse "HighScore" "Current high score.")
                    op.Tags <- singleTag "HighScore"
                    op)

                ensureOperation item OperationType.Post (fun _ ->
                    let op = OpenApiOperation(Summary = "Saves a new high score if it beats the current one.")
                    op.RequestBody <-
                        OpenApiRequestBody(
                            Required = true,
                            Content = dict [ "application/json", jsonContent (refSchema "HighScore") ]
                        )
                    op.Responses <- OpenApiResponses()
                    op.Responses.Add("200", ensureResponse "HighScore" "The stored high score.")
                    op.Tags <- singleTag "HighScore"
                    op))

            ensurePath "/api/vocabulary" (fun item ->
                ensureOperation item OperationType.Get (fun _ ->
                    let op = OpenApiOperation(Summary = "Gets a random vocabulary entry.")
                    op.Responses <- OpenApiResponses()
                    op.Responses.Add("200", ensureResponse "VocabularyEntry" "Random vocabulary entry.")
                    op.Tags <- singleTag "Vocabulary"
                    op))

let webApp =
    choose [ subRoute "/api" apiRoutes
             GET >=> htmlFile "wwwroot/index.html" ]

let builder = WebApplication.CreateBuilder()

builder.Services.AddGiraffe() |> ignore

builder.Services.AddSingleton<HighScoreStore>()
|> ignore

builder.Services.AddEndpointsApiExplorer() |> ignore
builder.Services.AddSwaggerGen(fun options ->
    options.SwaggerDoc(
        "v1",
        OpenApiInfo(
            Title = "Baltic Sea Eel API",
            Version = "v1",
            Description = "Endpoints for high scores and vocabulary used by the Baltic Sea Eel game."
        )
    )

    options.DocumentFilter<ApiDocumentFilter>())
|> ignore

let corsPolicyName = "AllowClient"

builder.Services.AddCors(fun options ->
    options.AddPolicy(
        corsPolicyName,
        fun policy ->
            policy.WithOrigins(
                      "http://localhost:5173",
                      "https://localhost:5173")
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  |> ignore))
|> ignore

let app = builder.Build()

if app.Environment.IsDevelopment() then
    app.UseSwagger() |> ignore
    app.UseSwaggerUI() |> ignore

app.UseDefaultFiles() |> ignore
app.UseStaticFiles() |> ignore
app.UseCors(corsPolicyName) |> ignore
app.UseGiraffe webApp |> ignore

app.Run()

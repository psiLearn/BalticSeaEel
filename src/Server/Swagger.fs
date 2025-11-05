namespace Eel.Server

open System.Collections.Generic
open Microsoft.OpenApi.Models
open Swashbuckle.AspNetCore.SwaggerGen

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

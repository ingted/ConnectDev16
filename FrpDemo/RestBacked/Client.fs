namespace RestBackend

open System
open WebSharper
open WebSharper.JavaScript
open WebSharper.JQuery
open WebSharper.UI.Next
open WebSharper.UI.Next.Client
open WebSharper.UI.Next.Html
open WebSharper.UI.Next.Notation
open WebSharper.UI.Next.Templating

module Resources =
    type CssResource() =
        inherit Resources.BaseResource("style.css")

    [<assembly:System.Web.UI.WebResource("style.css", "text/css")>]
    do ()

[<JavaScript>]
module Client =
    open RestApi

    type Result<'T> =
        | Success of 'T
        | Failure of message: string

    module Result =
        let map f res =
            match res with
            | Success r -> Success <| f r
            | Failure f -> Failure f

    let private toError s =
        s |> View.Map (function
            | Success _ -> None
            | Failure f -> Some f)

    let private validate pred msg v = 
        v |> View.Map (fun e -> if pred e then Success e else Failure msg)

    type private ApiData<'T> =
        {
            Type : RequestType
            Url  : string
            UrlParams : (string * string) seq
            Data : string option
            OnSuccess : string -> 'T
        }

    let private mkApiData t u up d ons =
        { 
            Type = t
            Url = u
            UrlParams = up
            Data = d
            OnSuccess = ons
        }

    let private apiCall data =
        Async.FromContinuations <| fun (ok, ko, _) ->
            let url = 
                let parts = 
                    data.UrlParams 
                    |> Seq.map (fun (a, b) -> a + "=" + b)
                    |> String.concat "&"
                data.Url + "?" + parts
            let settings =
                AjaxSettings(
                    Type = data.Type,
                    Url = url,
                    ContentType = "application/json",
                    DataType = DataType.Text,
                    Success = (fun (respData, _, _) ->
                        ok <| Success (data.OnSuccess (respData :?> string))),
                    Error = (fun (xhr, _, _) ->
                        ok <| Failure (Json.Deserialize<Error>(xhr.ResponseText).error))
                )
            data.Data |> Option.iter (fun d -> settings.Data <- d)
            JQuery.Ajax(settings) |> ignore

    let private FetchPeople () =
        mkApiData RequestType.GET "/api/people" [] None 
        <| Json.Deserialize<(Id * PersonData) []>
        |> apiCall

    let private GetPerson (id : Id) =
        mkApiData RequestType.GET ("/api/person/" + string id.id) [] None 
        <| Json.Deserialize<PersonData>
        |> apiCall

    let private PostPerson (data : PersonData) =
        mkApiData RequestType.POST "/api/person" []
        <| Some (Json.Serialize data)
        <| Json.Deserialize<Id>
        |> apiCall

    let private PutPerson (id : Id) (data : PersonData) =
        mkApiData RequestType.PUT ("/api/person/" + string id.id) []
        <| Some (Json.Serialize data)
        <| Json.Deserialize<unit>
        |> apiCall

    let private DeletePerson (id : Id) =
        mkApiData RequestType.DELETE ("/api/person/" + string id.id) [] None
        <| Json.Deserialize<unit>
        |> apiCall

    type PeopleInfo = Template<"../CommonResources/PeopleInfo.html">

    [<Require(typeof<Resources.CssResource>)>]
    let Main () =
        let people : ListModel<Id, Id * PersonData> = 
            ListModel.Create (fun (id, _) -> id) []

        async {
            let! res = FetchPeople ()
            match res with
            | Success ppl ->
                for p in ppl do
                    people.Add p
            | Failure f -> 
                Console.Log f
        }
        |> Async.Start

        let postPerson data =
            async {
                let! res = PostPerson data
                match res with
                | Success id ->
                    people.Add (id, data)
                | Failure f ->
                    Console.Log f
            }

        let deletePerson id =
            async {
                let! res = DeletePerson id
                match res with
                | Success () ->
                    people.RemoveByKey id
                | Failure f ->
                    Console.Log f
            }

        let isEmpty (s : string) = s.Trim () = ""
        let validDate f d =
            let d = Date.Parse(d)
            if JS.IsNaN d then Failure f
            else Success <| Date(d).Self

        let firstName = Var.Create ""
        let lastName = Var.Create ""

        let resultErr : Var<string option> = Var.Create None
        let submitPressed = Var.Create false

        let errors =
            [
                firstName.View |> validate (not << isEmpty) "First name cannot be empty."
                lastName.View |> validate (not << isEmpty) "Last name cannot be empty."
            ]
            |> Seq.map toError
            |> View.Sequence
            |> View.Map (Seq.choose id)

        let valid = errors |> View.Map Seq.isEmpty

        let submitButton valid =
            Doc.Button "Add" [] <| fun () ->
                submitPressed := true
                if valid then
                    let data =
                        { firstName = firstName.Value
                          lastName = lastName.Value }
                    async {
                        do! postPerson data
                        submitPressed := false
                    }
                    |> Async.Start

        PeopleInfo.Doc(
            FirstName = firstName,
            LastName = lastName,
            SubmitButton = [valid |> Doc.BindView submitButton],
            People = [
                people
                |> ListModel.View
                |> Doc.BindSeqCached (fun (id, pd) ->
                    PeopleInfo.Info.Doc(
                        FirstName = pd.firstName,
                        LastName = pd.lastName,
                        OnDeleteClick = (fun el ev ->
                            deletePerson id
                            |> Async.Start
                        )
                    )
                )
            ],
            Errors = [
                View.Map2 (fun (se : string option) errs ->
                    submitPressed.View 
                    |> View.Map (fun sp ->
                        if sp then // show errors only if submit was pressed
                            // append error from result (if any) to the list of errors
                            seq { yield! errs; if se.IsSome then yield se.Value }
                            |> Seq.map (fun e -> PeopleInfo.Error.Doc(Message = e))
                            |> Doc.Concat
                        else Doc.Empty
                    )
                ) resultErr.View errors
                |> View.Join
                |> Doc.EmbedView
            ]
        )

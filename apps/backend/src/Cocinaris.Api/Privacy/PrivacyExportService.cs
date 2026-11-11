using System.Diagnostics.Metrics;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Cocinaris.SharedKernel.Privacy;

namespace Cocinaris.Api.Privacy;

/// <summary>
/// Writes a person's export (PLAN-0049 Slice 1): a ZIP with <c>cocinaris-dados.json</c>, every row of
/// every table in <see cref="IAccountDataReader.Tables"/>, and <c>cocinaris-dados.html</c>, the main
/// parts in readable form. Both come from one snapshot and are streamed, never built in memory whole.
/// </summary>
/// <remarks>
/// Every value in the page is HTML-encoded and the page loads nothing and runs nothing: recipe text
/// and names are hostile input. Nothing exported reaches logs or metrics.
/// </remarks>
public sealed class PrivacyExportService(IAccountDataReader reader, TimeProvider timeProvider)
{
    /// <summary>Version of the JSON layout; raise it when a reader of an older file would misread the new one.</summary>
    public const int FormatVersion = 1;

    private static readonly Meter Meter = new("Cocinaris.Privacy");
    private static readonly Counter<long> Exports = Meter.CreateCounter<long>("cocinaris.privacy.export", "{export}", "Exports written, by outcome: completed or failed.");
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>The file name offered for download, dated in UTC.</summary>
    public string FileName() => $"cocinaris-dados-{timeProvider.GetUtcNow():yyyy-MM-dd}.zip";

    /// <summary>Writes the ZIP for one person to <paramref name="output"/>, which need not be seekable.</summary>
    /// <param name="ownerUserId">The person's internal user id, resolved by the caller from the session.</param>
    /// <param name="language">The page's language: <c>pt-BR</c>, <c>en</c>, or <c>es</c>; anything else reads as <c>pt-BR</c>.</param>
    /// <param name="output">The response body.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    public async Task WriteAsync(Guid ownerUserId, string? language, Stream output, CancellationToken cancellationToken)
    {
        try
        {
            await using var snapshot = await reader.OpenAsync(ownerUserId, cancellationToken);
            // Kestrel refuses synchronous writes; the archive still makes a few for headers, which the wrapper holds.
            await using var body = new AsyncFlushingStream(output);
            await using var zip = await ZipArchive.CreateAsync(body, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken);
            var generatedAt = timeProvider.GetUtcNow();
            Dictionary<string, int> counts;
            await using (var json = await zip.CreateEntry("cocinaris-dados.json", CompressionLevel.Optimal).OpenAsync(cancellationToken))
            {
                counts = await WriteJsonAsync(snapshot, ownerUserId, generatedAt, json, cancellationToken);
            }

            await using (var html = await zip.CreateEntry("cocinaris-dados.html", CompressionLevel.Optimal).OpenAsync(cancellationToken))
            {
                await new ExportPage(reader.Tables, snapshot, ExportText.For(language), counts, generatedAt).WriteAsync(html, cancellationToken);
            }

            await zip.DisposeAsync();
            await body.FlushAsync(cancellationToken);
            Exports.Add(1, new KeyValuePair<string, object?>("outcome", "completed"));
        }
        catch
        {
            Exports.Add(1, new KeyValuePair<string, object?>("outcome", "failed"));
            throw;
        }
    }

    private async Task<Dictionary<string, int>> WriteJsonAsync(IAccountDataSnapshot snapshot, Guid ownerUserId, DateTimeOffset generatedAt, Stream output, CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        await using var writer = new Utf8JsonWriter(output, WriterOptions);
        writer.WriteStartObject();
        writer.WriteString("format", "cocinaris-export");
        writer.WriteNumber("version", FormatVersion);
        writer.WriteString("generatedAt", generatedAt);
        writer.WriteString("accountId", ownerUserId);
        writer.WriteStartObject("modules");
        foreach (var module in reader.Tables.GroupBy(table => table.Module))
        {
            writer.WriteStartObject(module.Key);
            foreach (var table in module)
            {
                var count = 0;
                writer.WriteStartArray(table.Name);
                await foreach (var row in snapshot.ReadRowsAsync(table, cancellationToken))
                {
                    WriteRow(writer, row);
                    count++;
                    if (writer.BytesPending > 64 * 1024)
                    {
                        await writer.FlushAsync(cancellationToken);
                    }
                }

                writer.WriteEndArray();
                counts[table.QualifiedName] = count;
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
        await writer.FlushAsync(cancellationToken);
        return counts;
    }

    // Columns named "...Json" hold JSON as text; they are written as JSON so the file reads as one document.
    private static void WriteRow(Utf8JsonWriter writer, string row)
    {
        using var document = JsonDocument.Parse(row);
        writer.WriteStartObject();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            writer.WritePropertyName(property.Name);
            if (property.Name.EndsWith("Json", StringComparison.Ordinal) && property.Value.ValueKind == JsonValueKind.String && TryParse(property.Value.GetString(), out var nested))
            {
                using (nested)
                {
                    nested!.RootElement.WriteTo(writer);
                }
            }
            else
            {
                property.Value.WriteTo(writer);
            }
        }

        writer.WriteEndObject();
    }

    private static bool TryParse(string? text, out JsonDocument? document)
    {
        document = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            document = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>The readable page of an export: the main parts a person recognizes, then a count of everything.</summary>
internal sealed class ExportPage(IReadOnlyList<AccountDataTable> tables, IAccountDataSnapshot snapshot, ExportText text, IReadOnlyDictionary<string, int> counts, DateTimeOffset generatedAt)
{
    private static readonly HtmlEncoder Encoder = HtmlEncoder.Default;
    private readonly StringBuilder page = new();

    public async Task WriteAsync(Stream output, CancellationToken cancellationToken)
    {
        page.Append("<!doctype html><html lang=\"").Append(Encode(text.Language)).Append("\"><head><meta charset=\"utf-8\">")
            // The file is opened from disk; it must not load or run anything, whatever a recipe says.
            .Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>").Append(Encode(text["title"])).Append("</title>")
            .Append("<style>body{font:16px/1.5 system-ui,sans-serif;max-width:860px;margin:0 auto;padding:16px;color:#1f2a24;background:#fff}h1{font-size:1.6em}h2{margin-top:2em;border-bottom:1px solid #ccd;padding-bottom:.2em}table{border-collapse:collapse;width:100%;margin:.5em 0}th,td{text-align:left;vertical-align:top;border-bottom:1px solid #e3e6e4;padding:.3em .5em}th{font-weight:600}.muted{color:#5b6660}ol,ul{padding-left:1.4em}</style>")
            .Append("</head><body><h1>").Append(Encode(text["title"])).Append("</h1><p class=\"muted\">")
            .Append(Encode(text.Format("generated", generatedAt.ToString("yyyy-MM-dd HH:mm 'UTC'")))).Append("</p><p>").Append(Encode(text["intro"])).Append("</p>");

        await ProfileAsync(cancellationToken);
        await PantryAsync(cancellationToken);
        var recipeNames = await RecipesAsync(cancellationToken);
        await MenusAsync(recipeNames, cancellationToken);
        await CookedAsync(recipeNames, cancellationToken);
        Everything();
        page.Append("</body></html>");
        await using var writer = new StreamWriter(output, new UTF8Encoding(false));
        await writer.WriteAsync(page, cancellationToken);
    }

    private static readonly string[] ProfileFields = ["DisplayName", "Language", "Region", "TimeZone", "MeasurementSystem", "DefaultServingCount", "DefaultAdultCount", "DefaultChildCount", "OverallSkill", "TermsAcceptedAt"];

    private AccountDataTable Find(string qualified) => tables.Single(table => table.QualifiedName == qualified);

    private async Task<List<JsonElement>> RowsAsync(string qualified, CancellationToken cancellationToken)
    {
        var rows = new List<JsonElement>();
        await foreach (var row in snapshot.ReadRowsAsync(Find(qualified), cancellationToken))
        {
            using var document = JsonDocument.Parse(row);
            rows.Add(document.RootElement.Clone());
        }

        return rows;
    }

    private async Task ProfileAsync(CancellationToken cancellationToken)
    {
        Heading("profile");
        var profile = (await RowsAsync("profiles.user_profiles", cancellationToken)).FirstOrDefault();
        if (profile.ValueKind == JsonValueKind.Object)
        {
            Table(["field", "value"], ProfileFields
                .Where(field => !string.IsNullOrEmpty(Value(profile, field)))
                .Select(field => new[] { text[$"profile.{field}"], Value(profile, field) }));
        }
        else
        {
            Empty();
        }

        Subheading("preferences");
        var preferences = await RowsAsync("profiles.preference_entries", cancellationToken);
        var lists = await RowsAsync("profiles.ordered_code_entries", cancellationToken);
        var preferenceRows = preferences.Select(row => new[] { Value(row, "Category"), Value(row, "StableCode"), Value(row, "Presence"), Value(row, "Note") })
            .Concat(lists.Select(row => new[] { Value(row, "ListName"), Value(row, "StableCode"), string.Empty, string.Empty }))
            .ToList();
        if (preferenceRows.Count == 0) { Empty(); } else { Table(["category", "code", "presence", "note"], preferenceRows); }

        Subheading("equipment");
        var equipment = (await RowsAsync("profiles.equipment_entries", cancellationToken)).Where(row => !Flag(row, "IsRemoved")).ToList();
        if (equipment.Count == 0) { Empty(); }
        else
        {
            Table(["item", "capacity"], equipment.Select(row => new[] { First(Value(row, "CustomName"), Value(row, "StableCode")), Join(Value(row, "Capacity"), Value(row, "CapacityUnit")) }));
        }
    }

    private async Task PantryAsync(CancellationToken cancellationToken)
    {
        Heading("pantry");
        var products = (await RowsAsync("inventory.products", cancellationToken)).ToDictionary(row => Value(row, "Id"), row => Value(row, "DisplayName"));
        var lots = (await RowsAsync("inventory.lots", cancellationToken)).Where(row => Value(row, "DeletedAt").Length == 0).ToList();
        if (lots.Count == 0) { Empty(); return; }
        Table(["item", "quantity", "place", "state", "expires"], lots.Select(row => new[]
        {
            products.GetValueOrDefault(Value(row, "ProductId")) ?? string.Empty,
            Join(Value(row, "MeasuredValue"), Value(row, "MeasuredUnit")),
            First(Value(row, "CustomLocation"), Value(row, "StorageLocation")),
            Value(row, "LifecycleState"),
            Value(row, "PrintedExpirationDate"),
        }));
    }

    private async Task<Dictionary<string, string>> RecipesAsync(CancellationToken cancellationToken)
    {
        Heading("recipes");
        var recipes = await RowsAsync("recipes.recipes", cancellationToken);
        var current = recipes.ToDictionary(row => Value(row, "Id"), row => Value(row, "CurrentRevisionNumber"));
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var revisions = new List<JsonElement>();
        await foreach (var row in snapshot.ReadRowsAsync(Find("recipes.recipe_revisions"), cancellationToken))
        {
            using var document = JsonDocument.Parse(row);
            var revision = document.RootElement;
            // Only the current version is shown; every earlier one is in the JSON file.
            if (current.GetValueOrDefault(Value(revision, "RecipeId")) == Value(revision, "RevisionNumber"))
            {
                names[Value(revision, "RecipeId")] = Value(revision, "Name");
                revisions.Add(revision.Clone());
            }
        }

        if (revisions.Count == 0) { Empty(); return names; }
        var archived = recipes.Where(row => Value(row, "ArchivedAt").Length > 0).Select(row => Value(row, "Id")).ToHashSet();
        foreach (var revision in revisions.OrderBy(row => Value(row, "Name"), StringComparer.CurrentCulture))
        {
            page.Append("<h3>").Append(Encode(Value(revision, "Name"))).Append("</h3><p class=\"muted\">")
                .Append(Encode(text.Format("servings", Value(revision, "Servings"))));
            if (archived.Contains(Value(revision, "RecipeId")))
            {
                page.Append(" · ").Append(Encode(text["archived"]));
            }

            page.Append("</p>");
            var content = Content(revision);
            if (content.ValueKind == JsonValueKind.Object && content.TryGetProperty("ingredients", out var ingredients) && ingredients.ValueKind == JsonValueKind.Array)
            {
                page.Append("<ul>");
                foreach (var item in ingredients.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object))
                {
                    page.Append("<li>").Append(Encode(Join(Value(item, "requiredQuantity"), Value(item, "unit"), Value(item, "displayName")))).Append("</li>");
                }

                page.Append("</ul>");
            }

            if (content.ValueKind == JsonValueKind.Object && content.TryGetProperty("stages", out var stages) && stages.ValueKind == JsonValueKind.Array)
            {
                page.Append("<ol>");
                foreach (var stage in stages.EnumerateArray().Where(stage => stage.ValueKind == JsonValueKind.Object))
                {
                    page.Append("<li><strong>").Append(Encode(Value(stage, "name"))).Append("</strong> ").Append(Encode(Value(stage, "instructions"))).Append("</li>");
                }

                page.Append("</ol>");
            }
        }

        return names;
    }

    private async Task MenusAsync(IReadOnlyDictionary<string, string> recipeNames, CancellationToken cancellationToken)
    {
        Heading("menus");
        var menus = await RowsAsync("planning.menu_plans", cancellationToken);
        var entries = (await RowsAsync("planning.menu_entries", cancellationToken)).ToLookup(row => Value(row, "MenuPlanId"));
        if (menus.Count == 0) { Empty(); return; }
        foreach (var menu in menus.OrderByDescending(row => Value(row, "StartDate"), StringComparer.Ordinal))
        {
            page.Append("<h3>").Append(Encode(First(Value(menu, "Name"), text["menu"]))).Append("</h3><p class=\"muted\">")
                .Append(Encode(text.Format("period", Value(menu, "StartDate"), Value(menu, "EndDate")))).Append("</p>");
            var dishes = entries[Value(menu, "Id")].ToList();
            if (dishes.Count == 0) { Empty(); continue; }
            Table(["dish", "meal", "day", "servings"], dishes
                .OrderBy(row => Value(row, "PlannedDate"), StringComparer.Ordinal)
                .Select(row => new[] { recipeNames.GetValueOrDefault(Value(row, "RecipeId")) ?? string.Empty, Value(row, "MealType"), Value(row, "PlannedDate"), Value(row, "Servings") }));
        }
    }

    private async Task CookedAsync(IReadOnlyDictionary<string, string> recipeNames, CancellationToken cancellationToken)
    {
        Heading("cooked");
        var executions = await RowsAsync("recipes.recipe_executions", cancellationToken);
        if (executions.Count == 0) { Empty(); return; }
        Table(["dish", "when", "rating", "note"], executions
            .OrderByDescending(row => Value(row, "StartedAt"), StringComparer.Ordinal)
            .Select(row => new[] { recipeNames.GetValueOrDefault(Value(row, "RecipeId")) ?? string.Empty, First(Value(row, "FinishedAt"), Value(row, "StartedAt")), Value(row, "Rating"), Value(row, "Notes") }));
    }

    private void Everything()
    {
        Heading("everything");
        page.Append("<p>").Append(Encode(text["everything.intro"])).Append("</p>");
        Table(["part", "records"], tables.Select(table => new[] { table.QualifiedName, counts.GetValueOrDefault(table.QualifiedName).ToString(System.Globalization.CultureInfo.InvariantCulture) }));
    }

    // The recipe body is stored as text, sometimes wrapped in a "recipe" object.
    private static JsonElement Content(JsonElement revision)
    {
        if (!revision.TryGetProperty("NormalizedRecipeJson", out var raw) || raw.ValueKind != JsonValueKind.String)
        {
            return default;
        }

        try
        {
            using var document = JsonDocument.Parse(raw.GetString()!);
            var root = document.RootElement;
            return (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("recipe", out var inner) ? inner : root).Clone();
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private void Heading(string key) => page.Append("<h2>").Append(Encode(text[key])).Append("</h2>");

    private void Subheading(string key) => page.Append("<h3>").Append(Encode(text[key])).Append("</h3>");

    private void Empty() => page.Append("<p class=\"muted\">").Append(Encode(text["empty"])).Append("</p>");

    private void Table(IReadOnlyList<string> headers, IEnumerable<string[]> rows)
    {
        page.Append("<table><thead><tr>");
        foreach (var header in headers)
        {
            page.Append("<th scope=\"col\">").Append(Encode(text[$"column.{header}"])).Append("</th>");
        }

        page.Append("</tr></thead><tbody>");
        foreach (var row in rows)
        {
            page.Append("<tr>");
            foreach (var cell in row)
            {
                page.Append("<td>").Append(Encode(cell)).Append("</td>");
            }

            page.Append("</tr>");
        }

        page.Append("</tbody></table>");
    }

    private static string Encode(string value) => Encoder.Encode(value);

    private static string Value(JsonElement row, string name) =>
        row.ValueKind == JsonValueKind.Object && row.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
                _ => value.GetRawText(),
            }
            : string.Empty;

    private static bool Flag(JsonElement row, string name) => row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string First(params string[] values) => values.FirstOrDefault(value => value.Length > 0) ?? string.Empty;

    private static string Join(params string[] values) => string.Join(' ', values.Where(value => value.Length > 0));
}

/// <summary>Copy of the export's readable page in pt-BR, en, and es; the page is a file, not a screen of the app.</summary>
public sealed class ExportText
{
    private static readonly IReadOnlyDictionary<string, string> Portuguese = new Dictionary<string, string>
    {
        ["title"] = "Seus dados no Cocinaris",
        ["generated"] = "Gerado em {0}.",
        ["intro"] = "Esta página mostra as principais partes dos seus dados. O arquivo cocinaris-dados.json, no mesmo ZIP, tem tudo, inclusive as versões anteriores das receitas e o histórico.",
        ["profile"] = "Perfil",
        ["preferences"] = "Preferências e restrições",
        ["equipment"] = "Equipamentos",
        ["pantry"] = "Despensa",
        ["recipes"] = "Receitas",
        ["menus"] = "Cardápios",
        ["cooked"] = "O que você cozinhou",
        ["everything"] = "Tudo o que está no arquivo",
        ["everything.intro"] = "Quantos registros de cada parte estão em cocinaris-dados.json.",
        ["empty"] = "Nada aqui.",
        ["servings"] = "{0} porções",
        ["archived"] = "Arquivada",
        ["menu"] = "Cardápio",
        ["period"] = "De {0} a {1}",
        ["column.field"] = "Campo",
        ["column.value"] = "Valor",
        ["column.category"] = "Tipo",
        ["column.code"] = "Código",
        ["column.presence"] = "Situação",
        ["column.note"] = "Observação",
        ["column.item"] = "Item",
        ["column.capacity"] = "Capacidade",
        ["column.quantity"] = "Quantidade",
        ["column.place"] = "Onde",
        ["column.state"] = "Estado",
        ["column.expires"] = "Validade",
        ["column.dish"] = "Prato",
        ["column.meal"] = "Refeição",
        ["column.day"] = "Dia",
        ["column.servings"] = "Porções",
        ["column.when"] = "Quando",
        ["column.rating"] = "Nota",
        ["column.part"] = "Parte",
        ["column.records"] = "Registros",
        ["profile.DisplayName"] = "Nome",
        ["profile.Language"] = "Idioma",
        ["profile.Region"] = "Região",
        ["profile.TimeZone"] = "Fuso horário",
        ["profile.MeasurementSystem"] = "Medidas",
        ["profile.DefaultServingCount"] = "Porções",
        ["profile.DefaultAdultCount"] = "Adultos",
        ["profile.DefaultChildCount"] = "Crianças",
        ["profile.OverallSkill"] = "Experiência na cozinha",
        ["profile.TermsAcceptedAt"] = "Termos aceitos em",
    };

    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
    {
        ["title"] = "Your data in Cocinaris",
        ["generated"] = "Generated on {0}.",
        ["intro"] = "This page shows the main parts of your data. The file cocinaris-dados.json, in the same ZIP, has everything, including earlier versions of recipes and the history.",
        ["profile"] = "Profile",
        ["preferences"] = "Preferences and restrictions",
        ["equipment"] = "Equipment",
        ["pantry"] = "Pantry",
        ["recipes"] = "Recipes",
        ["menus"] = "Menus",
        ["cooked"] = "What you cooked",
        ["everything"] = "Everything in the file",
        ["everything.intro"] = "How many records of each part are in cocinaris-dados.json.",
        ["empty"] = "Nothing here.",
        ["servings"] = "{0} servings",
        ["archived"] = "Archived",
        ["menu"] = "Menu",
        ["period"] = "From {0} to {1}",
        ["column.field"] = "Field",
        ["column.value"] = "Value",
        ["column.category"] = "Kind",
        ["column.code"] = "Code",
        ["column.presence"] = "Status",
        ["column.note"] = "Note",
        ["column.item"] = "Item",
        ["column.capacity"] = "Capacity",
        ["column.quantity"] = "Quantity",
        ["column.place"] = "Where",
        ["column.state"] = "State",
        ["column.expires"] = "Expires",
        ["column.dish"] = "Dish",
        ["column.meal"] = "Meal",
        ["column.day"] = "Day",
        ["column.servings"] = "Servings",
        ["column.when"] = "When",
        ["column.rating"] = "Rating",
        ["column.part"] = "Part",
        ["column.records"] = "Records",
        ["profile.DisplayName"] = "Name",
        ["profile.Language"] = "Language",
        ["profile.Region"] = "Region",
        ["profile.TimeZone"] = "Time zone",
        ["profile.MeasurementSystem"] = "Measures",
        ["profile.DefaultServingCount"] = "Servings",
        ["profile.DefaultAdultCount"] = "Adults",
        ["profile.DefaultChildCount"] = "Children",
        ["profile.OverallSkill"] = "Cooking experience",
        ["profile.TermsAcceptedAt"] = "Terms accepted on",
    };

    private static readonly IReadOnlyDictionary<string, string> Spanish = new Dictionary<string, string>
    {
        ["title"] = "Tus datos en Cocinaris",
        ["generated"] = "Generado el {0}.",
        ["intro"] = "Esta página muestra las partes principales de tus datos. El archivo cocinaris-dados.json, en el mismo ZIP, lo tiene todo, incluidas las versiones anteriores de las recetas y el historial.",
        ["profile"] = "Perfil",
        ["preferences"] = "Preferencias y restricciones",
        ["equipment"] = "Equipos",
        ["pantry"] = "Despensa",
        ["recipes"] = "Recetas",
        ["menus"] = "Menús",
        ["cooked"] = "Lo que cocinaste",
        ["everything"] = "Todo lo que está en el archivo",
        ["everything.intro"] = "Cuántos registros de cada parte están en cocinaris-dados.json.",
        ["empty"] = "Nada aquí.",
        ["servings"] = "{0} porciones",
        ["archived"] = "Archivada",
        ["menu"] = "Menú",
        ["period"] = "Del {0} al {1}",
        ["column.field"] = "Campo",
        ["column.value"] = "Valor",
        ["column.category"] = "Tipo",
        ["column.code"] = "Código",
        ["column.presence"] = "Estado",
        ["column.note"] = "Nota",
        ["column.item"] = "Artículo",
        ["column.capacity"] = "Capacidad",
        ["column.quantity"] = "Cantidad",
        ["column.place"] = "Dónde",
        ["column.state"] = "Estado",
        ["column.expires"] = "Vence",
        ["column.dish"] = "Plato",
        ["column.meal"] = "Comida",
        ["column.day"] = "Día",
        ["column.servings"] = "Porciones",
        ["column.when"] = "Cuándo",
        ["column.rating"] = "Nota",
        ["column.part"] = "Parte",
        ["column.records"] = "Registros",
        ["profile.DisplayName"] = "Nombre",
        ["profile.Language"] = "Idioma",
        ["profile.Region"] = "Región",
        ["profile.TimeZone"] = "Huso horario",
        ["profile.MeasurementSystem"] = "Medidas",
        ["profile.DefaultServingCount"] = "Porciones",
        ["profile.DefaultAdultCount"] = "Adultos",
        ["profile.DefaultChildCount"] = "Niños",
        ["profile.OverallSkill"] = "Experiencia en la cocina",
        ["profile.TermsAcceptedAt"] = "Términos aceptados el",
    };

    private readonly IReadOnlyDictionary<string, string> copy;

    private ExportText(string language, IReadOnlyDictionary<string, string> copy)
    {
        Language = language;
        this.copy = copy;
    }

    /// <summary>Gets the page's language tag.</summary>
    public string Language { get; }

    /// <summary>Gets the copy for a key; a missing key shows the key, which the unit test forbids.</summary>
    public string this[string key] => copy.TryGetValue(key, out var value) ? value : key;

    /// <summary>The copy for a key with its placeholders filled.</summary>
    public string Format(string key, params object[] values) => string.Format(System.Globalization.CultureInfo.InvariantCulture, this[key], values);

    /// <summary>The copy for a language tag; anything but English or Spanish reads as Portuguese.</summary>
    public static ExportText For(string? language) => language switch
    {
        { } tag when tag.StartsWith("en", StringComparison.OrdinalIgnoreCase) => new("en", English),
        { } tag when tag.StartsWith("es", StringComparison.OrdinalIgnoreCase) => new("es", Spanish),
        _ => new("pt-BR", Portuguese),
    };

    /// <summary>Gets the keys of each language, for the test that keeps them equal.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyCollection<string>> Keys => new Dictionary<string, IReadOnlyCollection<string>>
    {
        ["pt-BR"] = Portuguese.Keys.ToList(),
        ["en"] = English.Keys.ToList(),
        ["es"] = Spanish.Keys.ToList(),
    };
}

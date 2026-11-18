using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace Ehr.Web.TagHelpers;

// A field and its error message, tied together for assistive technology (docs/brand: TextField). The framework's own
// helpers render the input and the message; these add what links them:
//
//   - the message of a field gets the id "<the input's id>-error", unless the page gave it one;
//   - an input, text area or select whose value was refused gets aria-invalid="true", and that id is added to its
//     aria-describedby, after any hint the page already named there.
//
// A page that sets an input's id itself gives the message the matching id.
static class FieldIds
{
    public const string ErrorSuffix = "-error";

    // The id the framework gives the input bound to this expression.
    public static string Of(ViewContext view, ModelExpression expression) =>
        TagBuilder.CreateSanitizedId(view.ViewData.TemplateInfo.GetFullHtmlFieldName(expression.Name), "_");

    public static bool IsRefused(ViewContext view, ModelExpression expression) =>
        view.ViewData.ModelState.TryGetValue(view.ViewData.TemplateInfo.GetFullHtmlFieldName(expression.Name), out var entry)
        && entry.Errors.Count > 0;
}

[HtmlTargetElement("input", Attributes = ForAttribute)]
[HtmlTargetElement("textarea", Attributes = ForAttribute)]
[HtmlTargetElement("select", Attributes = ForAttribute)]
public sealed class FieldStateTagHelper : TagHelper
{
    const string ForAttribute = "asp-for";

    [HtmlAttributeName(ForAttribute)]
    public ModelExpression For { get; set; } = null!;

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    // After the framework's helpers, which set the id.
    public override int Order => 1000;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (!FieldIds.IsRefused(ViewContext, For))
        {
            return;
        }

        var id = output.Attributes["id"]?.Value?.ToString() is { Length: > 0 } given ? given : FieldIds.Of(ViewContext, For);
        var described = output.Attributes["aria-describedby"]?.Value?.ToString();
        output.Attributes.SetAttribute("aria-invalid", "true");
        output.Attributes.SetAttribute("aria-describedby", string.IsNullOrEmpty(described) ? id + FieldIds.ErrorSuffix : $"{described} {id}{FieldIds.ErrorSuffix}");
    }
}

[HtmlTargetElement("span", Attributes = ForAttribute)]
public sealed class FieldErrorIdTagHelper : TagHelper
{
    const string ForAttribute = "asp-validation-for";

    [HtmlAttributeName(ForAttribute)]
    public ModelExpression For { get; set; } = null!;

    [ViewContext]
    [HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (!output.Attributes.ContainsName("id"))
        {
            output.Attributes.SetAttribute("id", FieldIds.Of(ViewContext, For) + FieldIds.ErrorSuffix);
        }
    }
}

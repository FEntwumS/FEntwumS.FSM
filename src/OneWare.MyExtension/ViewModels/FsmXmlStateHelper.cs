using System.Xml.Linq;

namespace OneWare.MyExtension.ViewModels;

public static class FsmXmlStateHelper
{
    public static string ResolveInitialStateId(XDocument? document, XNamespace ns)
    {
        if (document?.Root is null)
            return string.Empty;

        var startNodeTarget = document.Root.Element(ns + "startNode")?.Attribute("target")?.Value;
        if (!string.IsNullOrWhiteSpace(startNodeTarget))
            return startNodeTarget;

        return document.Root.Attribute("initial")?.Value ?? string.Empty;
    }

    public static void SyncInitialStateMetadata(XDocument document, XNamespace ns, IEnumerable<StateItemViewModel> states)
    {
        if (document.Root is null)
            return;

        var initialState = states.FirstOrDefault(state => state.IsInitialState) ?? states.FirstOrDefault();
        var initialStateId = initialState?.Id ?? string.Empty;

        document.Root.SetAttributeValue("initial", initialStateId);

        if (string.IsNullOrWhiteSpace(initialStateId))
        {
            document.Root.Element(ns + "startNode")?.Remove();
            return;
        }

        var startNode = document.Root.Element(ns + "startNode");
        if (startNode is null)
        {
            startNode = new XElement(ns + "startNode");
            document.Root.Add(startNode);
        }

        startNode.SetAttributeValue("target", initialStateId);
        startNode.SetAttributeValue("condition", startNode.Attribute("condition")?.Value ?? "1");

        EnsurePositionElement(startNode, ns + "conditionPosition", 293, 59);

        if (initialState is not null)
        {
            EnsurePositionElement(startNode, ns + "position", initialState.X - 92, initialState.Y - 70);
            EnsurePositionElement(startNode, ns + "targetPosition", initialState.X - 36, initialState.Y + 204);
        }
    }

    private static void EnsurePositionElement(XElement parent, XName elementName, double x, double y)
    {
        var element = parent.Element(elementName);
        if (element is null)
        {
            element = new XElement(elementName);
            parent.Add(element);
        }

        if (element.Attribute("x") is null)
            element.SetAttributeValue("x", (int)Math.Max(0, x));

        if (element.Attribute("y") is null)
            element.SetAttributeValue("y", (int)Math.Max(0, y));
    }
    public static void SetInitialState(XDocument document, XNamespace ns, StateItemViewModel initialState)
{
    if (document.Root is null) return;

    var targetId = initialState.Id;

    // 'initial' Attribut am <scxml> Root setzen/überschreiben
    document.Root.SetAttributeValue("initial", targetId);

    // startNode finden oder erstellen
    var startNode = document.Root.Element(ns + "startNode");
    if (startNode == null)
    {
        startNode = new XElement(ns + "startNode");
        // startNode am Anfang des Dokuments einfügen
        document.Root.AddFirst(startNode);
    }

    // Werte im startNode setzen
    startNode.SetAttributeValue("target", targetId);
    
    // Falls keine Condition da ist, Default auf "1"
    if (startNode.Attribute("condition") == null)
        startNode.SetAttributeValue("condition", "1");

    // Positionen berechnen (basierend auf deiner bestehenden Logik)
    EnsurePositionElement(startNode, ns + "conditionPosition", 293, 59);
    EnsurePositionElement(startNode, ns + "position", initialState.X - 92, initialState.Y - 70);
    EnsurePositionElement(startNode, ns + "targetPosition", initialState.X - 36, initialState.Y + 204);
}

    public static void RemoveInitialState(XDocument document, XNamespace ns)
    {
        if (document.Root is null) return;

        // 'initial' Attribut entfernen
        document.Root.Attribute("initial")?.Remove();

        // startNode entfernen
        document.Root.Element(ns + "startNode")?.Remove();
}}

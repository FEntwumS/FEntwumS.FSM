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
    }

    public static IEnumerable<TransitionViewModel> ReadTransitions(
        XElement stateElement,
        XNamespace ns,
        IReadOnlyDictionary<string, StateItemViewModel> statesById)
    {
        var sourceId = stateElement.Attribute("id")?.Value;
        if (string.IsNullOrWhiteSpace(sourceId) || !statesById.TryGetValue(sourceId, out var sourceState))
            yield break;

        var transitionsElement = stateElement.Element(ns + "transitions");
        if (transitionsElement is null)
            yield break;

        foreach (var transitionElement in transitionsElement.Elements(ns + "transition"))
        {
            var targetId = transitionElement.Attribute("target")?.Value;
            if (string.IsNullOrWhiteSpace(targetId) || !statesById.TryGetValue(targetId, out var targetState))
                continue;

            var ctrlPointsElement = transitionElement.Element(ns + "ctrlPoints");
            var controlPoints = ctrlPointsElement?.Elements(ns + "ctrlPoint").Select(ReadPoint).ToList()
                ?? new List<TransitionPointViewModel>();

            var startPoint = ReadPoint(transitionElement.Element(ns + "startPoint"));
            var endPoint = ReadPoint(transitionElement.Element(ns + "endPoint"));
            var conditionPosition = ReadPoint(transitionElement.Element(ns + "conditionPosition"));
            var isAutoRouted = bool.TryParse(transitionElement.Attribute("autoRoute")?.Value, out var parsedAutoRoute)
                ? parsedAutoRoute
                : controlPoints.Count == 0;

            var transition = new TransitionViewModel
            {
                SourceState = sourceState,
                TargetState = targetState,
                Condition = string.IsNullOrWhiteSpace(transitionElement.Attribute("cond")?.Value)
                    ? "1"
                    : transitionElement.Attribute("cond")!.Value,
                IsAutoRouted = isAutoRouted
            };

            transition.SourceSide = sourceState.GetNearestConnectorSide(startPoint.ToPoint());
            transition.TargetSide = targetState.GetNearestConnectorSide(endPoint.ToPoint());

            CopyPoint(startPoint, transition.StartPoint);
            CopyPoint(endPoint, transition.EndPoint);
            CopyPoint(conditionPosition, transition.ConditionPosition);

            foreach (var controlPoint in controlPoints)
                transition.ControlPoints.Add(controlPoint);

            if (!transition.IsAutoRouted)
                transition.InitializeManualAnchorsFromCurrentEndpoints();

            transition.RefreshGeometry();
            yield return transition;
        }
    }

    public static XElement CreateTransitionElement(TransitionViewModel transition, XNamespace ns)
    {
        return new XElement(ns + "transition",
            new XAttribute("cond", string.IsNullOrWhiteSpace(transition.Condition) ? "1" : transition.Condition),
            new XAttribute("target", transition.TargetState?.Id ?? string.Empty),
            new XAttribute("autoRoute", transition.IsAutoRouted),
            CreatePointElement(ns + "conditionPosition", transition.ConditionPosition),
            CreatePointElement(ns + "startPoint", transition.StartPoint),
            CreatePointElement(ns + "endPoint", transition.EndPoint),
            new XElement(ns + "ctrlPoints",
                transition.ControlPoints.Select(ctrlPoint => CreatePointElement(ns + "ctrlPoint", ctrlPoint))));
    }

    private static TransitionPointViewModel ReadPoint(XElement? element)
    {
        if (element is null)
            return new TransitionPointViewModel();

        var x = double.TryParse(element.Attribute("x")?.Value, out var parsedX) ? parsedX : 0;
        var y = double.TryParse(element.Attribute("y")?.Value, out var parsedY) ? parsedY : 0;
        return new TransitionPointViewModel(x, y);
    }

    private static void CopyPoint(TransitionPointViewModel source, TransitionPointViewModel target)
    {
        target.X = source.X;
        target.Y = source.Y;
    }

    private static XElement CreatePointElement(XName elementName, TransitionPointViewModel point)
    {
        return new XElement(elementName,
            new XAttribute("x", (int)point.X),
            new XAttribute("y", (int)point.Y));
    }
}

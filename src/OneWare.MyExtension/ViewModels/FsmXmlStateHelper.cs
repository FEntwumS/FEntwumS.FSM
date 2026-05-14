using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace OneWare.MyExtension.ViewModels;

public static class FsmXmlStateHelper
{
    private static readonly Regex OutputAssignmentPattern = new(
        @"^(?<signal>[^=]+?)\s*=\s*(?<expr>.+)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex BinaryValuePattern = new(
        "^[01]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static FsmGraphType ReadGraphType(XDocument? document, XNamespace ns)
    {
        var graphType = document?.Root?.Attribute("graph_type")?.Value;
        return string.Equals(graphType, "mealy", StringComparison.OrdinalIgnoreCase)
            ? FsmGraphType.Mealy
            : FsmGraphType.Moore;
    }

    public static void ApplyGraphType(XDocument document, FsmGraphType graphType)
    {
        document.Root?.SetAttributeValue("graph_type", graphType.ToString().ToLowerInvariant());
    }

    public static string ResolveInitialStateId(XDocument? document, XNamespace ns)
    {
        if (document?.Root is null)
            return string.Empty;

        var startNodeTarget = document.Root.Element(ns + "startNode")?.Attribute("target")?.Value;
        if (!string.IsNullOrWhiteSpace(startNodeTarget))
            return startNodeTarget;

        return document.Root.Attribute("initial")?.Value ?? string.Empty;
    }

    public static void SyncInitialStateMetadata(XDocument document, XNamespace ns, IEnumerable<StateItemViewModel> states, TransitionViewModel? initialTransition = null)
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
        startNode.SetAttributeValue("condition", initialTransition?.Condition ?? startNode.Attribute("condition")?.Value ?? string.Empty);

        if (initialTransition is not null)
        {
            SetOrUpdatePositionElement(startNode, ns + "position", initialTransition.StartPoint.X, initialTransition.StartPoint.Y);
            SetOrUpdatePositionElement(startNode, ns + "endPoint", initialTransition.EndPoint.X, initialTransition.EndPoint.Y);
            SetOrUpdatePositionElement(startNode, ns + "conditionPosition", initialTransition.ConditionPosition.X, initialTransition.ConditionPosition.Y);

            var ctrlPointsEl = startNode.Element(ns + "ctrlPoints");
            if (ctrlPointsEl is null)
            {
                ctrlPointsEl = new XElement(ns + "ctrlPoints");
                startNode.Add(ctrlPointsEl);
            }
            ctrlPointsEl.RemoveAll();
            foreach (var cp in initialTransition.ControlPoints)
                ctrlPointsEl.Add(new XElement(ns + "ctrlPoint", new XAttribute("x", (int)cp.X), new XAttribute("y", (int)cp.Y)));
        }
        else if (initialState is not null)
        {
            EnsurePositionElement(startNode, ns + "conditionPosition", 293, 59);
            EnsurePositionElement(startNode, ns + "position", initialState.X - 92, initialState.Y - 70);
            EnsurePositionElement(startNode, ns + "endPoint", initialState.X - 36, initialState.Y + 50);
        }
    }

    private static void SetOrUpdatePositionElement(XElement parent, XName elementName, double x, double y)
    {
        var element = parent.Element(elementName);
        if (element is null)
        {
            element = new XElement(elementName);
            parent.Add(element);
        }
        element.SetAttributeValue("x", (int)x);
        element.SetAttributeValue("y", (int)y);
    }

    public static TransitionViewModel? ReadInitialTransition(XDocument? document, XNamespace ns, IReadOnlyDictionary<string, StateItemViewModel> statesById)
    {
        var startNode = document?.Root?.Element(ns + "startNode");
        if (startNode is null)
            return null;

        var targetId = startNode.Attribute("target")?.Value;
        if (string.IsNullOrWhiteSpace(targetId) || !statesById.TryGetValue(targetId, out var targetState))
            return null;

        var position = startNode.Element(ns + "position");
        var endPointEl = startNode.Element(ns + "endPoint");
        var condPosEl = startNode.Element(ns + "conditionPosition");
        var ctrlPointsEl = startNode.Element(ns + "ctrlPoints");

        var startX = position is not null && double.TryParse(position.Attribute("x")?.Value, out var sx) ? sx : targetState.X - 80;
        var startY = position is not null && double.TryParse(position.Attribute("y")?.Value, out var sy) ? sy : targetState.Y + targetState.RenderHeight / 2.0;
        var endX = endPointEl is not null && double.TryParse(endPointEl.Attribute("x")?.Value, out var ex) ? ex : (double?)null;
        var endY = endPointEl is not null && double.TryParse(endPointEl.Attribute("y")?.Value, out var ey) ? ey : (double?)null;
        var condX = condPosEl is not null && double.TryParse(condPosEl.Attribute("x")?.Value, out var cx) ? cx : (startX + (endX ?? startX)) / 2.0;
        var condY = condPosEl is not null && double.TryParse(condPosEl.Attribute("y")?.Value, out var cy) ? cy : startY - 16;

        var condition = startNode.Attribute("condition")?.Value ?? string.Empty;

        var t = new TransitionViewModel
        {
            IsInitialTransition = true,
            TargetState = targetState,
            Condition = condition,
            IsAutoRouted = false
        };
        t.StartPoint.X = startX;
        t.StartPoint.Y = startY;

        if (ctrlPointsEl is not null)
        {
            foreach (var cp in ctrlPointsEl.Elements(ns + "ctrlPoint"))
            {
                var cpx = double.TryParse(cp.Attribute("x")?.Value, out var cpxv) ? cpxv : 0;
                var cpy = double.TryParse(cp.Attribute("y")?.Value, out var cpyv) ? cpyv : 0;
                t.ControlPoints.Add(new TransitionPointViewModel(cpx, cpy));
            }
        }

        t.RefreshGeometry(); // computes EndPoint based on StartPoint → TargetState

        // Override endPoint and conditionPosition only if they were saved
        if (endX.HasValue && endY.HasValue)
        {
            t.EndPoint.X = endX.Value;
            t.EndPoint.Y = endY.Value;
        }
        t.ConditionPosition.X = condX;
        t.ConditionPosition.Y = condY;

        return t;
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

    public static IReadOnlyList<string> ReadFinalStateIds(XDocument? document, XNamespace ns)
    {
        if (document?.Root is null)
            return Array.Empty<string>();

        return document.Root
            .Elements(ns + "finalState")
            .Select(el => el.Attribute("id")?.Value ?? string.Empty)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();
    }

    public static void SyncFinalStatesMetadata(XDocument document, XNamespace ns, IEnumerable<StateItemViewModel> states)
    {
        if (document.Root is null)
            return;

        // Remove all existing <finalState> elements from root
        document.Root.Elements(ns + "finalState").Remove();

        // Re-add one <finalState id="..."/> per final state
        foreach (var state in states.Where(s => s.IsFinalState))
            document.Root.Add(new XElement(ns + "finalState", new XAttribute("id", state.Id)));
    }

    public static void RemoveInitialState(XDocument document, XNamespace ns)    {
        if (document.Root is null) return;

        // 'initial' Attribut entfernen
        document.Root.Attribute("initial")?.Remove();

        // startNode entfernen
        document.Root.Element(ns + "startNode")?.Remove();
    }

    public static IEnumerable<TransitionViewModel> ReadTransitions(
        XElement stateElement,
        XNamespace ns,
        IReadOnlyDictionary<string, StateItemViewModel> statesById,
        IEnumerable<SignalDefinitionViewModel> signals,
        FsmGraphType graphType)
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
                OutputAssignments = graphType == FsmGraphType.Mealy
                    ? ReadTransitionOutputAssignments(transitionElement, ns, signals)
                    : string.Empty,
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

    public static XElement CreateTransitionElement(TransitionViewModel transition, XNamespace ns, IEnumerable<SignalDefinitionViewModel> signals, FsmGraphType graphType)
    {
        var transitionElement = new XElement(ns + "transition",
            new XAttribute("cond", string.IsNullOrWhiteSpace(transition.Condition) ? "1" : transition.Condition),
            new XAttribute("target", transition.TargetState?.Id ?? string.Empty),
            new XAttribute("autoRoute", transition.IsAutoRouted),
            CreatePointElement(ns + "conditionPosition", transition.ConditionPosition),
            CreatePointElement(ns + "startPoint", transition.StartPoint),
            CreatePointElement(ns + "endPoint", transition.EndPoint),
            new XElement(ns + "ctrlPoints",
                transition.ControlPoints.Select(ctrlPoint => CreatePointElement(ns + "ctrlPoint", ctrlPoint))));

        if (graphType == FsmGraphType.Mealy)
        {
            foreach (var assignElement in CreateAssignElements(transition.OutputAssignments, ns, signals))
                transitionElement.Add(assignElement);
        }

        return transitionElement;
    }

    public static string ReadTransitionOutputAssignments(XElement transitionElement, XNamespace ns, IEnumerable<SignalDefinitionViewModel> signals)
    {
        return ReadAssignments(transitionElement.Elements(ns + "assign"), signals);
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

    public static string ReadOutputAssignments(XElement stateElement, XNamespace ns, IEnumerable<SignalDefinitionViewModel> signals)
    {
        return ReadAssignments(
            stateElement.Element(ns + "during")?.Elements(ns + "assign"),
            signals);
    }

    public static XElement CreateDuringElement(StateItemViewModel state, XNamespace ns, IEnumerable<SignalDefinitionViewModel> signals)
    {
        var duringElement = new XElement(ns + "during");

        foreach (var assignElement in CreateAssignElements(state.OutputAssignments, ns, signals))
            duringElement.Add(assignElement);

        return duringElement;
    }

    private static string ReadAssignments(IEnumerable<XElement>? assignmentElements, IEnumerable<SignalDefinitionViewModel> signals)
    {
        var assignments = (assignmentElements ?? Enumerable.Empty<XElement>())
            .Select(assign => (
                Signal: assign.Attribute("signal")?.Value?.Trim() ?? string.Empty,
                Expression: assign.Attribute("expr")?.Value?.Trim() ?? string.Empty))
            .Where(assignment => !string.IsNullOrWhiteSpace(assignment.Signal) && !string.IsNullOrWhiteSpace(assignment.Expression))
            .ToList();

        if (assignments is null || assignments.Count == 0)
            return string.Empty;

        var outputSignals = GetOutputSignals(signals).ToList();
        if (outputSignals.Count == 0)
            return string.Join(Environment.NewLine, assignments.Select(assignment => assignment.Expression));

        var formattedOutputs = outputSignals
            .Select(outputSignal => assignments.FirstOrDefault(assignment => string.Equals(assignment.Signal, outputSignal.Name, StringComparison.OrdinalIgnoreCase)))
            .Where(assignment => assignment.Signal is not null)
            .Select(assignment => FormatOutputValue(assignment.Expression!, outputSignals.First(outputSignal => string.Equals(outputSignal.Name, assignment.Signal, StringComparison.OrdinalIgnoreCase))))
            .Where(value => !string.IsNullOrWhiteSpace(value));

        return string.Join(Environment.NewLine, formattedOutputs);
    }

    private static IEnumerable<XElement> CreateAssignElements(string outputAssignments, XNamespace ns, IEnumerable<SignalDefinitionViewModel> signals)
    {
        var lines = outputAssignments
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var outputSignals = GetOutputSignals(signals).ToList();
        if (outputSignals.Count == 0)
            return Array.Empty<XElement>();

        var assignElements = new List<XElement>();

        for (var index = 0; index < lines.Length && index < outputSignals.Count; index++)
        {
            var signal = outputSignals[index];
            var expression = ParseDisplayValue(lines[index]);
            if (string.IsNullOrWhiteSpace(expression))
                continue;

            assignElements.Add(new XElement(ns + "assign",
                new XAttribute("signal", signal.Name.Trim()),
                new XAttribute("expr", expression)));
        }

        return assignElements;
    }

    public static string NormalizeOutputAssignments(string outputAssignments, IEnumerable<SignalDefinitionViewModel> signals)
    {
        if (string.IsNullOrWhiteSpace(outputAssignments))
            return string.Empty;

        var outputSignals = GetOutputSignals(signals).ToList();
        if (outputSignals.Count == 0)
            return outputAssignments.Trim();

        var lines = outputAssignments
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var normalizedValues = outputSignals
            .Select((signal, index) => index < lines.Length ? FormatOutputValue(ParseDisplayValue(lines[index]), signal) : null)
            .Where(value => !string.IsNullOrWhiteSpace(value));

        return string.Join(Environment.NewLine, normalizedValues);
    }

    public static IReadOnlyList<SignalDefinitionViewModel> ReadSignals(XDocument? document, XNamespace ns)
    {
        if (document?.Root is null)
            return Array.Empty<SignalDefinitionViewModel>();

        return document.Root
            .Element(ns + "signals")?
            .Elements(ns + "signal")
            .Select(signalElement =>
            {
                var xmlType = signalElement.Attribute("type")?.Value?.Trim() ?? string.Empty;
                var xmlSize = signalElement.Attribute("size")?.Value?.Trim() ?? string.Empty;

                // Reverse-map backend XML types to the UI "bit_n" type with the correct size.
                string uiType, uiSize;
                if (string.Equals(xmlType, "nibble", StringComparison.OrdinalIgnoreCase))
                {
                    uiType = "bit_n";
                    uiSize = "4";
                }
                else if (string.Equals(xmlType, "byte", StringComparison.OrdinalIgnoreCase))
                {
                    uiType = "bit_n";
                    uiSize = "8";
                }
                else if (string.Equals(xmlType, "vector", StringComparison.OrdinalIgnoreCase))
                {
                    uiType = "bit_n";
                    uiSize = xmlSize; // preserve whatever size was stored
                }
                else
                {
                    uiType = xmlType; // e.g. "bit"
                    uiSize = xmlSize;
                }

                return new SignalDefinitionViewModel
                {
                    Name = signalElement.Attribute("name")?.Value?.Trim() ?? string.Empty,
                    Direction = signalElement.Attribute("dir")?.Value?.Trim() ?? string.Empty,
                    Type = uiType,
                    Size = uiSize
                };
            })
            .Where(signal => !string.IsNullOrWhiteSpace(signal.Name))
                .ToList()
                ?? new List<SignalDefinitionViewModel>();
    }

    public static void SyncSignalsMetadata(XDocument document, XNamespace ns, IEnumerable<SignalDefinitionViewModel> signals)
    {
        if (document.Root is null)
            return;

        var signalsElement = document.Root.Element(ns + "signals");
        if (signalsElement is null)
        {
            signalsElement = new XElement(ns + "signals");

            var variablesElement = document.Root.Element(ns + "variables");
            if (variablesElement is not null)
                variablesElement.AddBeforeSelf(signalsElement);
            else
                document.Root.AddFirst(signalsElement);
        }

        signalsElement.RemoveAll();

        foreach (var signal in signals.Where(signal => !string.IsNullOrWhiteSpace(signal.Name)))
        {
            var signalElement = new XElement(ns + "signal",
                new XAttribute("name", signal.Name.Trim()));

            if (!string.IsNullOrWhiteSpace(signal.Direction))
                signalElement.SetAttributeValue("dir", signal.Direction.Trim());

            if (!string.IsNullOrWhiteSpace(signal.Type))
            {
                if (string.Equals(signal.Type, "bit_n", StringComparison.OrdinalIgnoreCase))
                {
                    // Map bit_n to the correct backend XML type based on size.
                    // nibble = 4 bits, byte = 8 bits, vector = everything else (needs a size attribute).
                    if (signal.Size == "4")
                    {
                        signalElement.SetAttributeValue("type", "nibble");
                        // no size attribute for nibble
                    }
                    else if (signal.Size == "8")
                    {
                        signalElement.SetAttributeValue("type", "byte");
                        // no size attribute for byte
                    }
                    else
                    {
                        signalElement.SetAttributeValue("type", "vector");
                        if (!string.IsNullOrWhiteSpace(signal.Size))
                            signalElement.SetAttributeValue("size", signal.Size.Trim());
                    }
                }
                else
                {
                    signalElement.SetAttributeValue("type", signal.Type.Trim());
                    // bit type has fixed size 1 — no size attribute needed
                }
            }

            signalsElement.Add(signalElement);
        }
    }

    private static IEnumerable<SignalDefinitionViewModel> GetOutputSignals(IEnumerable<SignalDefinitionViewModel> signals)
    {
        return signals.Where(signal => signal.IsOutput && !string.IsNullOrWhiteSpace(signal.Name));
    }

    private static string FormatOutputValue(string expression, SignalDefinitionViewModel signal)
    {
        var trimmedExpression = expression.Trim();
        if (BinaryValuePattern.IsMatch(trimmedExpression))
            return NormalizeBinaryWidth(trimmedExpression, signal.BitWidth);

        if (int.TryParse(trimmedExpression, out var decimalValue))
        {
            var binaryValue = Convert.ToString(Math.Max(0, decimalValue), 2);
            return NormalizeBinaryWidth(binaryValue, signal.BitWidth);
        }

        return trimmedExpression;
    }

    private static string ParseDisplayValue(string displayValue)
    {
        var trimmedValue = displayValue.Trim();
        if (string.IsNullOrWhiteSpace(trimmedValue))
            return string.Empty;

        if (BinaryValuePattern.IsMatch(trimmedValue))
            return Convert.ToInt32(trimmedValue, 2).ToString();

        var match = OutputAssignmentPattern.Match(trimmedValue);
        if (match.Success)
            return match.Groups["expr"].Value.Trim();

        return trimmedValue;
    }

    private static string NormalizeBinaryWidth(string binaryValue, int bitWidth)
    {
        var normalizedWidth = Math.Max(1, bitWidth);
        return binaryValue.Length >= normalizedWidth
            ? binaryValue
            : binaryValue.PadLeft(normalizedWidth, '0');
    }
}

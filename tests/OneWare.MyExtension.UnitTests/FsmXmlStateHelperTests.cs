using System;
using System.Linq;
using System.Xml.Linq;
using OneWare.MyExtension.ViewModels;
using Xunit;

namespace OneWare.MyExtension.UnitTests;

public class FsmXmlStateHelperTests
{
    [Fact]
    public void ResolveInitialStateId_PrefersStartNodeTarget()
    {
        XNamespace ns = "http://www.w3.org/2005/07/scxml";
        var doc = XDocument.Parse(@"<scxml xmlns='http://www.w3.org/2005/07/scxml' initial='IDLE'><startNode target='OFF' condition='1' /></scxml>");

        var initialStateId = FsmXmlStateHelper.ResolveInitialStateId(doc, ns);

        Assert.Equal("OFF", initialStateId);
    }

    [Fact]
    public void SyncInitialStateMetadata_WritesInitialAndStartNode()
    {
        XNamespace ns = "http://www.w3.org/2005/07/scxml";
        var doc = XDocument.Parse(@"<scxml xmlns='http://www.w3.org/2005/07/scxml'><states /></scxml>");
        var states = new[]
        {
            new StateItemViewModel
            {
                Id = "OFF",
                X = 368,
                Y = 128,
                Width = 144,
                Height = 64,
                IsInitialState = true
            }
        };

        FsmXmlStateHelper.SyncInitialStateMetadata(doc, ns, states);

        Assert.Equal("OFF", doc.Root?.Attribute("initial")?.Value);

        var startNode = doc.Root?.Element(ns + "startNode");
        Assert.NotNull(startNode);
        Assert.Equal("OFF", startNode?.Attribute("target")?.Value);
        Assert.Equal("1", startNode?.Attribute("condition")?.Value);
    }

    [Fact]
    public void ReadGraphType_ReturnsMealyWhenConfigured()
    {
        XNamespace ns = "http://www.w3.org/2005/07/scxml";
        var doc = XDocument.Parse(@"<scxml xmlns='http://www.w3.org/2005/07/scxml' graph_type='mealy' />");

        var graphType = FsmXmlStateHelper.ReadGraphType(doc, ns);

        Assert.Equal(FsmGraphType.Mealy, graphType);
    }

    [Fact]
    public void ApplyGraphType_WritesGraphTypeAttribute()
    {
        var doc = XDocument.Parse(@"<scxml xmlns='http://www.w3.org/2005/07/scxml' />");

        FsmXmlStateHelper.ApplyGraphType(doc, FsmGraphType.Mealy);

        Assert.Equal("mealy", doc.Root?.Attribute("graph_type")?.Value);
    }

    [Fact]
    public void ReadOutputAssignments_ReturnsAssignLines()
    {
        XNamespace ns = "http://www.w3.org/2005/07/scxml";
        var stateElement = XElement.Parse(@"<state xmlns='http://www.w3.org/2005/07/scxml' id='OFF'>
  <during>
    <assign signal='L' expr='0' />
    <assign signal='M' expr='7' />
  </during>
</state>");
                var signals = new[]
                {
                        new SignalDefinitionViewModel { Name = "L", Direction = "out", Type = "bit" },
                        new SignalDefinitionViewModel { Name = "M", Direction = "out", Type = "vector", Size = "3" }
                };

                var outputs = FsmXmlStateHelper.ReadOutputAssignments(stateElement, ns, signals);

                Assert.Equal("0" + Environment.NewLine + "111", outputs);
    }

    [Fact]
    public void CreateDuringElement_WritesAssignElementsFromStateOutputs()
    {
        XNamespace ns = "http://www.w3.org/2005/07/scxml";
        var signals = new[]
        {
            new SignalDefinitionViewModel { Name = "L", Direction = "out", Type = "bit" },
            new SignalDefinitionViewModel { Name = "M", Direction = "out", Type = "vector", Size = "3" }
        };
        var state = new StateItemViewModel
        {
            OutputAssignments = "0" + Environment.NewLine + "111"
        };

        var duringElement = FsmXmlStateHelper.CreateDuringElement(state, ns, signals);

        var assignElements = duringElement.Elements(ns + "assign").ToList();
        Assert.Equal(2, assignElements.Count);
        Assert.Equal("L", assignElements[0].Attribute("signal")?.Value);
        Assert.Equal("0", assignElements[0].Attribute("expr")?.Value);
        Assert.Equal("M", assignElements[1].Attribute("signal")?.Value);
        Assert.Equal("7", assignElements[1].Attribute("expr")?.Value);
    }

    [Fact]
    public void ReadTransitionOutputAssignments_ReturnsAssignLines()
    {
        XNamespace ns = "http://www.w3.org/2005/07/scxml";
        var transitionElement = XElement.Parse(@"<transition xmlns='http://www.w3.org/2005/07/scxml' cond='K' target='ON'>
  <assign signal='L' expr='3' />
  <assign signal='M' expr='1' />
</transition>");
        var signals = new[]
        {
            new SignalDefinitionViewModel { Name = "L", Direction = "out", Type = "vector", Size = "2" },
            new SignalDefinitionViewModel { Name = "M", Direction = "out", Type = "bit" }
        };

        var outputs = FsmXmlStateHelper.ReadTransitionOutputAssignments(transitionElement, ns, signals);

        Assert.Equal("11" + Environment.NewLine + "1", outputs);
    }

    [Fact]
    public void CreateTransitionElement_WritesAssignElementsForMealyOutputs()
    {
        XNamespace ns = "http://www.w3.org/2005/07/scxml";
        var signals = new[]
        {
            new SignalDefinitionViewModel { Name = "L", Direction = "out", Type = "vector", Size = "3" }
        };
        var sourceState = new StateItemViewModel { Id = "OFF" };
        var targetState = new StateItemViewModel { Id = "ON" };
        var transition = new TransitionViewModel
        {
            SourceState = sourceState,
            TargetState = targetState,
            Condition = "K",
            OutputAssignments = "111"
        };

        var transitionElement = FsmXmlStateHelper.CreateTransitionElement(transition, ns, signals, FsmGraphType.Mealy);

        var assignElement = Assert.Single(transitionElement.Elements(ns + "assign"));
        Assert.Equal("L", assignElement.Attribute("signal")?.Value);
        Assert.Equal("7", assignElement.Attribute("expr")?.Value);
    }

    [Fact]
    public void NormalizeOutputAssignments_FormatsValuesBySignalWidth()
    {
        var signals = new[]
        {
            new SignalDefinitionViewModel { Name = "L", Direction = "out", Type = "bit" },
            new SignalDefinitionViewModel { Name = "M", Direction = "out", Type = "vector", Size = "3" }
        };

        var outputs = FsmXmlStateHelper.NormalizeOutputAssignments("0" + Environment.NewLine + "7", signals);

        Assert.Equal("0" + Environment.NewLine + "111", outputs);
    }

    [Fact]
    public void ReadSignals_ReturnsRootSignalDefinitions()
    {
        XNamespace ns = "http://www.w3.org/2005/07/scxml";
        var doc = XDocument.Parse(@"<scxml xmlns='http://www.w3.org/2005/07/scxml'>
  <signals>
    <signal name='K' dir='in' type='bit' />
    <signal name='L' dir='out' type='vector' size='3' />
  </signals>
</scxml>");

        var signals = FsmXmlStateHelper.ReadSignals(doc, ns);

        Assert.Equal(2, signals.Count);
        Assert.Equal("K", signals[0].Name);
        Assert.Equal("in", signals[0].Direction);
        Assert.Equal("bit", signals[0].Type);
        Assert.Equal("L", signals[1].Name);
        Assert.Equal("out", signals[1].Direction);
        Assert.Equal("vector", signals[1].Type);
        Assert.Equal("3", signals[1].Size);
    }

    [Fact]
    public void SyncSignalsMetadata_WritesSignalsElement()
    {
        XNamespace ns = "http://www.w3.org/2005/07/scxml";
        var doc = XDocument.Parse(@"<scxml xmlns='http://www.w3.org/2005/07/scxml'><variables /></scxml>");
        var signals = new[]
        {
            new SignalDefinitionViewModel { Name = "K", Direction = "in", Type = "bit" },
            new SignalDefinitionViewModel { Name = "L", Direction = "out", Type = "vector", Size = "3" }
        };

        FsmXmlStateHelper.SyncSignalsMetadata(doc, ns, signals);

        var signalElements = doc.Root?.Element(ns + "signals")?.Elements(ns + "signal").ToList();
        Assert.NotNull(signalElements);
        Assert.Equal(2, signalElements!.Count);
        Assert.Equal("K", signalElements[0].Attribute("name")?.Value);
        Assert.Equal("in", signalElements[0].Attribute("dir")?.Value);
        Assert.Equal("bit", signalElements[0].Attribute("type")?.Value);
        Assert.Equal("L", signalElements[1].Attribute("name")?.Value);
        Assert.Equal("out", signalElements[1].Attribute("dir")?.Value);
        Assert.Equal("vector", signalElements[1].Attribute("type")?.Value);
        Assert.Equal("3", signalElements[1].Attribute("size")?.Value);
    }
}

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
}

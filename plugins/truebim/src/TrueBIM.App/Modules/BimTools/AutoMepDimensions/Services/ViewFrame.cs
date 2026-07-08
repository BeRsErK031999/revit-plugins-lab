using Autodesk.Revit.DB;

namespace TrueBIM.App.Modules.BimTools.AutoMepDimensions.Services;

public sealed class ViewFrame
{
    private ViewFrame(XYZ right, XYZ up, XYZ normal)
    {
        Right = right;
        Up = up;
        Normal = normal;
    }

    public XYZ Right { get; }

    public XYZ Up { get; }

    public XYZ Normal { get; }

    public static ViewFrame Create(View view)
    {
        XYZ right = view.RightDirection.Normalize();
        XYZ up = view.UpDirection.Normalize();
        XYZ normal = view.ViewDirection.Normalize();
        return new ViewFrame(right, up, normal);
    }

    public double ToX(XYZ point)
    {
        return point.DotProduct(Right);
    }

    public double ToY(XYZ point)
    {
        return point.DotProduct(Up);
    }

    public double ToNormal(XYZ point)
    {
        return point.DotProduct(Normal);
    }

    public XYZ FromCoordinates(double x, double y, double normal)
    {
        return Right.Multiply(x) + Up.Multiply(y) + Normal.Multiply(normal);
    }
}

namespace Aegis.Cafeteria.UI;

public sealed class WhiteTextActionButton : Button
{
    protected override void OnPaint(PaintEventArgs eventArgs)
    {
        Color backgroundColor = this.BackColor;
        Color borderColor = this.FlatAppearance.BorderColor.IsEmpty
            ? ControlPaint.Dark(backgroundColor)
            : this.FlatAppearance.BorderColor;

        using SolidBrush backgroundBrush = new(backgroundColor);
        eventArgs.Graphics.FillRectangle(backgroundBrush, this.ClientRectangle);

        if (this.FlatAppearance.BorderSize > 0)
        {
            using Pen borderPen = new(borderColor, this.FlatAppearance.BorderSize);
            Rectangle borderRectangle = new
            (
                  0
                , 0
                , Math.Max(0, this.ClientSize.Width - 1)
                , Math.Max(0, this.ClientSize.Height - 1)
            );
            eventArgs.Graphics.DrawRectangle(borderPen, borderRectangle);
        }

        TextRenderer.DrawText
        (
              eventArgs.Graphics
            , this.Text
            , this.Font
            , this.ClientRectangle
            , Color.White
            , TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis
        );

        if (this.Focused && this.ShowFocusCues)
        {
            Rectangle focusRectangle = Rectangle.Inflate(this.ClientRectangle, -4, -4);
            ControlPaint.DrawFocusRectangle(eventArgs.Graphics, focusRectangle, Color.White, backgroundColor);
        }
    }
}

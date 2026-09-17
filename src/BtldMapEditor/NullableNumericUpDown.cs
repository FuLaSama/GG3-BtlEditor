/*
 * NullableNumericUpDown.cs
 *
 * 可空数字框：空文本 = null，对应「这个 FB 槽没写 / 用户清空」。
 * 有数字则 SetScalar 写入（包括 0，0 会留下槽）。
 * 高度固定，避免 TableLayout 行高跟着字号跳。
 */
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace BtldMapEditor
{
    /// <summary>
    /// 基于 TextBox 的可空数值编辑；高度固定，避免 TableLayout 行高跳变。
    /// </summary>
    public class NullableNumericUpDown : TextBox
    {
        private decimal _minimum = -32768;
        private decimal _maximum = 65535;
        private decimal? _lastFiredValue = null;
        private bool _isInternalUpdating = false;
        private bool _userEnabled = true;

        public event EventHandler ValueChanged;

        public NullableNumericUpDown()
        {
            this.TextAlign = HorizontalAlignment.Left;
            this.BorderStyle = BorderStyle.Fixed3D;
            this.AutoSize = false;
            this.Height = Math.Max(26, Font.Height + 10);
            UpdateVisualState();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            this.Height = Math.Max(26, Font.Height + 10);
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            int h = Math.Max(26, Font.Height + 10);
            Size s = base.GetPreferredSize(proposedSize);
            return new Size(s.Width, h);
        }

        public new bool Enabled
        {
            get => _userEnabled;
            set
            {
                _userEnabled = value;
                base.TabStop = value;
                UpdateVisualState();
            }
        }

        private void UpdateVisualState()
        {
            if (_userEnabled)
            {
                this.BackColor = SystemColors.Window;
                this.ForeColor = SystemColors.WindowText;
                this.Cursor = Cursors.IBeam;
            }
            else
            {
                this.BackColor = SystemColors.Control;
                this.ForeColor = SystemColors.GrayText;
                this.Cursor = Cursors.Default;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (!_userEnabled)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (!_userEnabled)
            {
                e.Handled = true;
                return;
            }
            base.OnKeyPress(e);
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_CONTEXTMENU = 0x007B;
            const int WM_PASTE = 0x0302;
            const int WM_CUT = 0x0300;
            const int WM_CLEAR = 0x0301;

            if (!_userEnabled)
            {
                if (m.Msg == WM_CONTEXTMENU || m.Msg == WM_PASTE || m.Msg == WM_CUT || m.Msg == WM_CLEAR)
                {
                    return;
                }
            }
            base.WndProc(ref m);
        }

        [Category("Data")]
        public decimal Minimum
        {
            get => _minimum;
            set => _minimum = value;
        }

        [Category("Data")]
        public decimal Maximum
        {
            get => _maximum;
            set => _maximum = value;
        }

        [Category("Data")]
        public int DecimalPlaces { get; set; } = 0;

        [Category("Data")]
        public decimal Increment { get; set; } = 1;

        [Category("Data")]
        public decimal Value
        {
            get => NullableValue ?? Minimum;
            set => NullableValue = value;
        }

        [Category("Data")]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public decimal? NullableValue
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Text)) return null;
                if (decimal.TryParse(Text.Trim(), out decimal val))
                {
                    return val;
                }
                return null;
            }
            set
            {
                _isInternalUpdating = true;
                if (value == null)
                {
                    Text = "";
                }
                else
                {
                    decimal clamped = Math.Max(Minimum, Math.Min(Maximum, value.Value));
                    Text = clamped.ToString();
                }
                _isInternalUpdating = false;
                // 程序赋值只同步基线，不触发 ValueChanged，避免点选格子时把旧控件值写回 Stage。
                _lastFiredValue = NullableValue;
            }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            if (!_isInternalUpdating)
            {
                CheckAndFireValueChanged();
            }
        }

        protected override void OnLeave(EventArgs e)
        {
            base.OnLeave(e);
            if (!string.IsNullOrWhiteSpace(Text))
            {
                if (decimal.TryParse(Text.Trim(), out decimal val))
                {
                    decimal clamped = Math.Max(Minimum, Math.Min(Maximum, val));
                    if (clamped != val)
                    {
                        _isInternalUpdating = true;
                        Text = clamped.ToString();
                        _isInternalUpdating = false;
                    }
                }
                else
                {
                    _isInternalUpdating = true;
                    Text = "";
                    _isInternalUpdating = false;
                }
                CheckAndFireValueChanged();
            }
        }

        private void CheckAndFireValueChanged()
        {
            decimal? current = NullableValue;
            if (current != _lastFiredValue)
            {
                _lastFiredValue = current;
                ValueChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}

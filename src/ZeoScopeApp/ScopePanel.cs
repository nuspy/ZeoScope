﻿// Copyright 2011 dancodru
// Licensed under the Apache License, Version 2.0 (the "License");
// You may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// http://www.apache.org/licenses/LICENSE-2.0
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

namespace ZeoScope
{
    using System;
    using System.ComponentModel;
    using System.Diagnostics;
    using System.Drawing;
    using System.IO;
    using System.Windows.Forms;

    using SlimDX;
    using SlimDX.Direct3D9;

    using DXFont = SlimDX.Direct3D9.Font;
    using WinFont = System.Drawing.Font;

    internal struct VectorColored
    {
        public const VertexFormat Format = VertexFormat.PositionRhw | VertexFormat.Diffuse;
        public const int StrideSize = 20;

        public Vector4 Position;
        public int Color;

        public VectorColored(float x, float y, Color color)
        {
            this.Position = new Vector4(x, y, 0f, 1f);
            this.Color = color.ToArgb();
        }
    }

    internal class ScopePanel : Panel
    {
        #region Privates
        private int borderMargin = 5;

        private HScrollBar scrollBar;
        private Panel splitterPanel;

        private Panel devicePanel;
        private ToolTip toolTip;

        private Device device;
        private VectorColored[][] scopeVerts;
        private int scopeVertsLen;
        private DXFont titleFont;
        private DXFont labelFont;

        private Color lineColor = Color.FromArgb(140, 130, 105);
        private Color backgroundColor = Color.FromArgb(30, 80, 120);
        #endregion

        #region Constructor
        public ScopePanel()
            : base()
        {
            this.devicePanel = new Panel();
            this.splitterPanel = new Panel();
            this.scrollBar = new HScrollBar();
            this.toolTip = new ToolTip();

            this.Controls.Add(this.devicePanel);
            this.Controls.Add(this.scrollBar);
            this.Controls.Add(this.splitterPanel);

            this.devicePanel.Dock = DockStyle.Fill;
            this.devicePanel.BackColor = Color.Black;
            this.devicePanel.Location = new System.Drawing.Point(0, 0);
            this.devicePanel.MouseMove += new MouseEventHandler(this.DevicePanel_MouseMove);
            this.devicePanel.MouseClick += new MouseEventHandler(this.DevicePanel_MouseClick);
            this.devicePanel.MouseEnter += new EventHandler(this.ScopePanel_MouseEnter);

            this.splitterPanel.Dock = DockStyle.Bottom;
            this.splitterPanel.Size = new Size(this.Width, 3);

            this.scrollBar.Dock = DockStyle.Bottom;
            this.scrollBar.Minimum = 0;
            this.scrollBar.Value = 0;
            this.scrollBar.SmallChange = 0;
            this.scrollBar.LargeChange = 0;
            this.scrollBar.Maximum = 0;
            this.scrollBar.Size = new Size(this.Width, 17);
            this.scrollBar.Scroll += new ScrollEventHandler(this.ScrollBar_Scroll);
            this.scrollBar.ValueChanged += new EventHandler(this.ScrollBar_ValueChanged);

            try
            {
                this.CreateDevice();
            }
            catch (FileNotFoundException ex)
            {
                throw new ZeoException(ex, "Install DirectX Runtime from http://www.microsoft.com/download/en/details.aspx?id=35");
            }
        }
        #endregion

        #region Event Declarations
        [Category("Action")]
        public event ScrollEventHandler ScrollScope;

        [Category("Action")]
        public event EventHandler ScrollValueChanged;

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Browsable(false)]
        public event MouseEventHandler DeviceMouseMove;

        [EditorBrowsable(EditorBrowsableState.Never)]
        [Browsable(false)]
        public event MouseEventHandler DeviceMouseClick;
        #endregion

        #region Properties
        [Category("Appearance")]
        [RefreshProperties(RefreshProperties.Repaint)]
        public string Title { get; set; }

        [Category("Appearance")]
        [RefreshProperties(RefreshProperties.Repaint)]
        public int HorizontalLinesCount { get; set; }

        [Category("Appearance")]
        [DefaultValue(true)]
        [RefreshProperties(RefreshProperties.Repaint)]
        public bool ScrollBarVisible
        {
            get
            {
                return this.scrollBar.Visible;
            }

            set
            {
                this.scrollBar.Visible = value;
            }
        }

        [Category("Appearance")]
        [DefaultValue(true)]
        [RefreshProperties(RefreshProperties.Repaint)]
        public bool SplitterPanelVisible
        {
            get
            {
                return this.splitterPanel.Visible;
            }

            set
            {
                this.splitterPanel.Visible = value;
            }
        }

        public int ScrollBarValue
        {
            get
            {
                return this.scrollBar.Value;
            }

            set
            {
                if (this.scrollBar.Visible == false)
                {
                    return;
                }

                if (value < 0)
                {
                    value = 0;
                }

                // see MSDN for ScrollBar.Maximum
                int max = this.scrollBar.Maximum + 1 - this.scrollBar.LargeChange;
                max = max > 0 ? max : 0;

                double mid = this.ScopeLength / 2 / this.SamplesPerSecond;
                int p = (int)(value - mid);
                int x = 0;

                if (p < 0)
                {
                    this.scrollBar.Value = 0;
                    x = (int)(value * this.SamplesPerSecond);
                }
                else if (p >= 0 && p <= max)
                {
                    this.scrollBar.Value = p;
                    x = (int)(mid * this.SamplesPerSecond);
                }
                else
                {
                    this.scrollBar.Value = max;
                    x = (int)((value - max) * this.SamplesPerSecond);
                }

                if (this.scrollBar.Focused == false)
                {
                    this.ScopeX = x;
                    this.ScopeX = this.ScopeX > 0 ? this.ScopeX : 0;
                    if (this.ScopeData != null && this.ScopeX >= this.ScopeData.Length)
                    {
                        this.ScopeX = this.ScopeData.Length - 1;
                    }
                }
            }
        }

        public int ScrollBarMaximum
        {
            get
            {
                return this.scrollBar.Maximum;
            }

            set
            {
                // see MSDN for ScrollBar.Maximum
                if (value > 2)
                {
                    this.scrollBar.Maximum = value - 2;
                }
                else
                {
                    this.scrollBar.Maximum = 0;
                }
            }
        }

        public int ScopeLength
        {
            get
            {
                int length = this.devicePanel.Width - this.borderMargin - this.borderMargin;
                return (length > 0) ? length : 0;
            }
        }

        public int ScopeX { get; set; }

        public ChannelData[] ScopeData { get; set; }

        public double SamplesPerSecond { get; set; }

        public int NumberOfChannels { get; set; }

        public float[] MaxValueDisplay { get; set; }

        public float[] MinValueDisplay { get; set; }

        public Color[] GraphColors { get; set; }

        public string[] LabelFormatStrings { get; set; }

        public int LabelSpacing { get; set; }

        public string TimeString { get; set; }
        #endregion

        #region Events
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            this.RenderDevice();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            base.OnPaintBackground(e);
            this.RenderDevice();
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            this.scopeVerts = null;
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            this.scopeVerts = null;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing == true)
            {
                if (this.titleFont != null)
                {
                    this.titleFont.Dispose();
                    this.titleFont = null;
                }

                if (this.labelFont != null)
                {
                    this.labelFont.Dispose();
                    this.labelFont = null;
                }

                if (this.device != null && this.device.Disposed == false)
                {
                    if (this.device.Direct3D != null)
                    {
                        this.device.Direct3D.Dispose();
                    }

                    this.device.Dispose();
                    this.device = null;
                }
            }
        }

        private void ScopePanel_MouseEnter(object sender, EventArgs e)
        {
            if (this.scrollBar.Visible == true)
            {
                this.scrollBar.Focus();
            }
            else
            {
                this.Focus();
            }
        }

        private void ScrollBar_ValueChanged(object sender, EventArgs e)
        {
            if (this.ScrollValueChanged != null)
            {
                this.ScrollValueChanged(this, e);
            }
        }

        private void ScrollBar_Scroll(object sender, ScrollEventArgs e)
        {
            this.toolTip.SetToolTip(this.scrollBar, this.TimeString);

            if (this.ScrollScope != null)
            {
                this.ScrollScope(this, e);
            }
        }

        private void DevicePanel_MouseMove(object sender, MouseEventArgs e)
        {
            if (this.DeviceMouseMove != null)
            {
                this.DeviceMouseMove(this, e);
            }

            this.ScopeX = e.X - this.borderMargin;

            if (this.ScopeData != null && this.ScopeX >= this.ScopeData.Length)
            {
                this.ScopeX = this.ScopeData.Length - 1;
            }

            if (this.ScopeX < 0)
            {
                this.ScopeX = 0;
            }
        }

        private void DevicePanel_MouseClick(object sender, MouseEventArgs e)
        {
            if (this.DeviceMouseClick != null)
            {
                this.DeviceMouseClick(this, e);
            }
        }
        #endregion

        #region Rendering
        private void CreateDevice()
        {
            this.device = new Device(new Direct3D(), 0, DeviceType.Hardware, this.devicePanel.Handle, CreateFlags.HardwareVertexProcessing, new PresentParameters()
            {
                SwapEffect = SwapEffect.Discard,
                Windowed = true,
                BackBufferWidth = this.devicePanel.ClientSize.Width,
                BackBufferHeight = this.devicePanel.ClientSize.Height
            });

            this.device.VertexFormat = VectorColored.Format;

            this.titleFont = new DXFont(this.device, new WinFont("Courier New", 14, FontStyle.Bold));
            this.labelFont = new DXFont(this.device, new WinFont("Courier New", 10, FontStyle.Bold));
        }

        public void RenderDevice()
        {
            if (this.devicePanel.Width == 0 || this.devicePanel.Height == 0)
            {
                return;
            }

            try
            {
                if (this.device == null || this.device.Disposed == true || this.device.Viewport == null)
                {
                    this.CreateDevice();
                }
            }
            catch (NullReferenceException)
            {
                this.CreateDevice();
            }

            if (this.device.Viewport.Width != this.devicePanel.Width || this.device.Viewport.Height != this.devicePanel.Height
                || this.device.Direct3D == null)
            {
                this.titleFont.Dispose();
                this.labelFont.Dispose();

                if (this.device.Direct3D != null)
                {
                    this.device.Direct3D.Dispose();
                }

                this.device.Dispose();

                this.CreateDevice();
            }

            if (this.Width != 0 && this.Height != 0)
            {
                this.DrawBackground();
                this.DrawScope();
                this.DrawText();
                this.device.Present();
            }
        }

        private void DrawBackground()
        {
            int maxX = this.devicePanel.Width;
            int maxY = this.devicePanel.Height;

            this.device.Clear(ClearFlags.Target, this.backgroundColor, 0f, 0);

            VectorColored[] verts = new VectorColored[5];
            verts[0] = new VectorColored(this.borderMargin, this.borderMargin, Color.Black);
            verts[1] = new VectorColored(maxX - this.borderMargin, this.borderMargin, Color.Black);
            verts[2] = new VectorColored(maxX - this.borderMargin, maxY - this.borderMargin, Color.Black);
            verts[3] = new VectorColored(this.borderMargin, maxY - this.borderMargin, Color.Black);
            verts[4] = new VectorColored(this.borderMargin, this.borderMargin, Color.Black);

            this.device.BeginScene();
            this.device.DrawUserPrimitives(PrimitiveType.TriangleStrip, 3, verts);
            this.device.EndScene();

            int borderVertsLen = maxX - this.borderMargin - this.borderMargin + maxY - this.borderMargin - this.borderMargin;

            verts = new VectorColored[borderVertsLen];

            int xlen = (maxX - this.borderMargin - this.borderMargin) / 2;
            int ylen = (maxY - this.borderMargin - this.borderMargin) / 2;
            if (xlen <= 0 || ylen <= 0)
            {
                return;
            }

            for (int i = 0; i < xlen; i++)
            {
                verts[i] = new VectorColored(this.borderMargin + (i * 2), this.borderMargin, this.lineColor);
            }

            for (int i = 0; i < xlen; i++)
            {
                verts[i + xlen] = new VectorColored(this.borderMargin + (i * 2), maxY - this.borderMargin - 1, this.lineColor);
            }

            for (int i = 0; i < ylen; i++)
            {
                verts[i + (2 * xlen)] = new VectorColored(this.borderMargin, this.borderMargin + (i * 2), this.lineColor);
            }

            for (int i = 0; i < ylen; i++)
            {
                verts[i + (2 * xlen) + ylen] = new VectorColored(maxX - this.borderMargin, this.borderMargin + (i * 2), this.lineColor);
            }

            this.device.BeginScene();
            this.device.DrawUserPrimitives(PrimitiveType.PointList, verts.Length, verts);
            this.device.EndScene();

            // Horizontal lines
            int horisontalGridLineLendth = (maxX - this.borderMargin - this.borderMargin) / 2;

            int lineSpace = (maxY - this.borderMargin - this.borderMargin) / (this.HorizontalLinesCount + 1);
            for (int j = 1; j <= this.HorizontalLinesCount; j++)
            {
                verts = new VectorColored[horisontalGridLineLendth];
                for (int i = 0; i < xlen; i++)
                {
                    if (this.HorizontalLinesCount > 1 && this.HorizontalLinesCount % 2 == 1 &&
                        j == (this.HorizontalLinesCount + 1) / 2)
                    {
                        verts[i] = new VectorColored(this.borderMargin + (i * 2), (lineSpace * j) + this.borderMargin, Color.White);
                        verts[i].Color = Color.White.ToArgb();
                    }
                    else
                    {
                        verts[i] = new VectorColored(this.borderMargin + (i * 2), (lineSpace * j) + this.borderMargin, this.lineColor);
                    }
                }

                this.device.BeginScene();
                this.device.DrawUserPrimitives(PrimitiveType.PointList, verts.Length, verts);
                this.device.EndScene();
            }
        }

        private void DrawScope()
        {
            if (this.ScopeData == null)
            {
                return;
            }

            int maxX = this.devicePanel.Width;
            int maxY = this.devicePanel.Height;

            if (this.scopeVerts == null || this.scopeVerts.Length != this.NumberOfChannels)
            {
                this.scopeVertsLen = this.ScopeLength;
                this.scrollBar.LargeChange = (int)(this.scopeVertsLen / this.SamplesPerSecond);
                this.scrollBar.SmallChange = (this.scrollBar.LargeChange > 10) ? this.scrollBar.LargeChange / 10 : 1;

                this.scopeVerts = new VectorColored[this.NumberOfChannels][];
                for (int i = 0; i < this.NumberOfChannels; i++)
                {
                    this.scopeVerts[i] = new VectorColored[this.scopeVertsLen];
                }
            }

            int len = this.CalculatePulseVerts();

            if (len > 0)
            {
                this.device.BeginScene();
                for (int i = this.NumberOfChannels - 1; i >= 0; i--)
                {
                    this.device.DrawUserPrimitives(PrimitiveType.LineStrip, len - 1, this.scopeVerts[i]);
                }

                this.device.EndScene();
            }

            // Draw cursor line
            int cursorMargin = 25;
            int ylen = (maxY - cursorMargin - cursorMargin) / 2;
            if (ylen > 0)
            {
                VectorColored[] verts = new VectorColored[ylen];
                for (int i = 0; i < ylen; i++)
                {
                    verts[i] = new VectorColored(this.ScopeX + this.borderMargin, cursorMargin + 4 + (i * 2), this.lineColor);
                }

                this.device.BeginScene();
                this.device.DrawUserPrimitives(PrimitiveType.PointList, verts.Length, verts);
                this.device.EndScene();
            }
        }

        private int CalculatePulseVerts()
        {
            float maxX = (float)this.devicePanel.Width;
            float maxY = (float)this.devicePanel.Height;

            float[] scopeMod = new float[this.NumberOfChannels];
            float[] scopeZero = new float[this.NumberOfChannels];

            for (int k = 0; k < this.NumberOfChannels; k++)
            {
                scopeMod[k] = (this.MaxValueDisplay[k] - this.MinValueDisplay[k]) / 2.0f;
                scopeZero[k] = this.MinValueDisplay[k] + scopeMod[k];
            }

            float mod = (maxY - this.borderMargin - this.borderMargin - 4) / 2;
            float zero = mod + this.borderMargin + 2;

            int len = (this.scopeVertsLen <= this.ScopeData.Length) ? this.scopeVertsLen : this.ScopeData.Length;
            int i = 0;
            for (i = 0; i < len; i++)
            {
                float x = maxX - this.borderMargin - this.scopeVertsLen + i;

                for (int k = 0; k < this.NumberOfChannels; k++)
                {
                    float y = zero;
                    if (this.ScopeData[i] != null)
                    {
                        y = zero - (mod * ((this.ScopeData[i].Values[k] - scopeZero[k]) / scopeMod[k]));
                        this.scopeVerts[k][i] = new VectorColored(x, y, this.GraphColors[k]);
                    }
                    else
                    {
                        goto end;
                    }
                }
            }

        end:;
            return i;
        }

        private void DrawText()
        {
            int textX = 10;
            int textY = 10;

            this.device.BeginScene();

            // Draw Title
            this.titleFont.DrawString(null, this.Title, textX, textY, Color.Gray);

            if (this.ScopeData != null && this.ScopeData.Length > 0)
            {
                if (this.ScopeX >= this.ScopeData.Length)
                {
                    this.ScopeX = this.ScopeData.Length - 1;
                }

                int textLength = this.titleFont.MeasureString(null, this.Title, DrawTextFormat.ExpandTabs).Width;
                textX += textLength + 20;

                // Draw time label
                if (string.IsNullOrEmpty(this.TimeString) == false)
                {
                    this.labelFont.DrawString(null, this.TimeString, textX, textY, Color.Gray);
                    textX += 90;
                }

                for (int i = 0; i < this.NumberOfChannels; i++)
                {
                    if (this.ScopeData[this.ScopeX] != null)
                    {
                        if (this.LabelFormatStrings[i].StartsWith("\t"))
                        {
                            textX += 70;
                        }

                        this.labelFont.DrawString(null, string.Format(this.LabelFormatStrings[i], this.ScopeData[this.ScopeX].Values[i]),
                            textX, textY, this.GraphColors[i]);

                        textX += this.LabelSpacing;
                    }
                }
            }

            this.device.EndScene();
        }
        #endregion
    }
}

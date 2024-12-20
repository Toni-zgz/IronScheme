/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Microsoft Public License. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the  Microsoft Public License, please send an email to 
 * dlr@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Microsoft Public License.
 *
 * You must not remove this notice, or any other, from this software.
 *
 *
 * ***************************************************************************/

using System;
using System.Threading;
using System.IO;
using Microsoft.Scripting.Hosting;
using Microsoft.Scripting.Utils;

namespace Microsoft.Scripting.Shell
{

    public class BasicConsole : IConsole, IDisposable {

        private TextWriter _output;
        private TextWriter _errorOutput;
        private AutoResetEvent _ctrlCEvent;
        private Thread _creatingThread;

        protected TextWriter Output {
            get { return _output; }
            set { _output = value; }
        }

        protected TextWriter ErrorOutput {
            get { return _errorOutput; }
            set { _errorOutput = value; }
        }
        
        protected AutoResetEvent CtrlCEvent {
            get { return _ctrlCEvent; }
            set { _ctrlCEvent = value; }
        }
        
        protected Thread CreatingThread {
            get { return _creatingThread; }
            set { _creatingThread = value; }
        }

        private ConsoleColor _promptColor = ConsoleColor.Gray;
        private ConsoleColor _outColor = ConsoleColor.Gray;
        private ConsoleColor _errorColor = ConsoleColor.Gray;
        private ConsoleColor _warningColor = ConsoleColor.Gray;

        public BasicConsole(IScriptEngine engine, bool colorful) {
            Contract.RequiresNotNull(engine, "engine");

            SetupColors(colorful);

            _creatingThread = Thread.CurrentThread;
            _output = engine.GetOutputWriter(false);
            
            _errorOutput = engine.GetOutputWriter(true);

            Console.CancelKeyPress += new ConsoleCancelEventHandler(delegate(object sender, ConsoleCancelEventArgs e) {
                if (e.SpecialKey == ConsoleSpecialKey.ControlC) {
                    e.Cancel = true;
                    _ctrlCEvent.Set();
                    _creatingThread.Abort(new KeyboardInterruptException(""));
                }
            });
            _ctrlCEvent = new AutoResetEvent(false);
        }

        private void SetupColors(bool colorful) {

            if (colorful) {
                _promptColor = ConsoleColor.Cyan;
                _outColor = ConsoleColor.Green;
                _errorColor = ConsoleColor.Red;
                _warningColor = ConsoleColor.Yellow;
            } else {
              _promptColor = _outColor = _errorColor = _warningColor = ConsoleColor.Gray;
            }
        }

        protected void WriteColor(TextWriter output, string str, ConsoleColor c)
        {
          if (string.IsNullOrEmpty(str))
          {
            return;
          }

          if (this is SuperConsole)
          {
            ConsoleColor origColor = Console.ForegroundColor;
            try
            {
              Console.ForegroundColor = c;
              string[] tokens = str.Split('\n');


              for (int j = 0; j < tokens.Length; j++)
              {
                str = tokens[j];

              TRYAGAIN:

                int space = Console.BufferWidth - Console.CursorLeft;
                if (!string.IsNullOrEmpty(str) && str.Length > space)
                {
                  // now find the 'line break'
                  int i = space - 1;
                  for (; i >= 0; i--)
                  {
                    if (char.IsSeparator(str[i]))
                    {
                      break;
                    }
                  }

                  if (i > 0)
                  {
                    output.WriteLine(str.Substring(0, i));
                    str = str.Substring(i);
                    goto TRYAGAIN;
                  }
                  else
                  {
                    output.Write(str.Substring(0, space));
                    str = str.Substring(space);
                    goto TRYAGAIN;
                  }
                }

                if (j < tokens.Length - 1)
                {
                  output.WriteLine(str);
                }
                else
                {
                  output.Write(str);
                }
                output.Flush();
              }

            }
            finally
            {
              Console.ForegroundColor = origColor;
            }
          }
          else
          {
            output.Write(str);
            output.Flush();
          }
        }

        #region IConsole Members

        public virtual string ReadLine(int autoIndentSize) {
            Write("".PadLeft(autoIndentSize), Style.Prompt);

            string res = Console.In.ReadLine();
            if (res == null) {
                // we have a race - the Ctrl-C event is delivered
                // after ReadLine returns.  We need to wait for a little
                // bit to see which one we got.  This will cause a slight
                // delay when shutting down the process via ctrl-z, but it's
                // not really perceptible.  In the ctrl-C case we will return
                // as soon as the event is signaled.
                if (_ctrlCEvent != null && _ctrlCEvent.WaitOne(100, false)) {
                    // received ctrl-C
                    return "";
                } else {
                    // received ctrl-Z
                    return null;
                }
            }
            return "".PadLeft(autoIndentSize) + res;
        }

        public virtual void Write(string text, Style style) {
            switch (style) {
                case Style.Prompt: WriteColor(_output, text, _promptColor); break;
                case Style.Out: WriteColor(_output, text, _outColor); break;
                case Style.Error: WriteColor(_errorOutput, text, _errorColor); break;
                case Style.Warning: WriteColor(_errorOutput, text, _warningColor); break;
            }
        }

        public void WriteLine(string text, Style style) {
            Write(text + Environment.NewLine, style);
        }

        public void WriteLine() {
            Write(Environment.NewLine, Style.Out);
        }

        #endregion

        #region IDisposable Members

        public void Dispose() {
            if (_ctrlCEvent != null) {
                _ctrlCEvent.Close();
            }

            GC.SuppressFinalize(this);
        }

        #endregion
    }

}


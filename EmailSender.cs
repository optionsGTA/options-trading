using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Timers;
using Ecng.Common;
using OptionBot.robot;

namespace OptionBot
{
    class EmailSender : Disposable {
        readonly static Logger _log = new Logger();
        readonly string _address;
        readonly Controller _controller;
        readonly Timer _timer;

        readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(5);
        readonly TimeSpan _emailInterval = TimeSpan.FromSeconds(30);

        readonly List<Tuple<DateTime, string, string>> _emailBuffer = new List<Tuple<DateTime, string, string>>();

        DateTime _lastSentTime;

        public string Address {get {return _address;}}

        Connector Connector {get {return _controller.Connector;}}

        public EmailSender(Controller controller, string address) {
            address = address.Trim();
            if(!VerifyEmailAddress(address)) throw new ArgumentException("address is invalid");

            _controller = controller;
            _address = address;

            _timer = new Timer {
                AutoReset = true,
                Interval = _checkInterval.TotalMilliseconds
            };
            _timer.Elapsed += OnTick;
            _timer.Start();
        }

        protected override void DisposeManaged() {
            _timer.Stop();
            _timer.Dispose();

            base.DisposeManaged();
        }

        public void SendEmail(string subject, string text = null, bool separateMessage = false) {
            if(!separateMessage) {
                lock(_emailBuffer)
                    _emailBuffer.Add(Tuple.Create(Connector.GetMarketTime(), subject, text));
            } else {
                Task.Run(() => {
                    SendEmailInternal(subject, text);
                });
            }
        }

        public void SendAtmChange(OptionSeriesInfo si, OptionStrikeInfo oldAtmCall, OptionStrikeInfo oldAtmPut) {
            SendEmail($"ATM change for {si.SeriesId.StrFutDate}: {si.AtmCall?.Strike}/{si.AtmPut?.Strike}", 
                        $"ATM strike changed for option series {si.SeriesId}:\nCall: {oldAtmCall?.Strike} ==> {si.AtmCall?.Strike}\nPut: {oldAtmPut?.Strike} ==> {si.AtmPut?.Strike}",
                        true);
        }

        void OnTick(object sender, ElapsedEventArgs args) {
            try {
                var now = Connector.GetMarketTime();
                if(now - _lastSentTime < _emailInterval)
                    return;

                Tuple<DateTime, string, string>[] toSend;
                lock(_emailBuffer) {
                    toSend = _emailBuffer.ToArray();
                    _emailBuffer.Clear();
                }

                if(toSend.Length == 0)
                    return;

                _lastSentTime = now;

                if(IsDisposed) return;

                if(toSend.Length == 1) {
                    var subject = toSend[0].Item2;
                    var body = toSend[0].Item3;
                    var message = "{0:dd-MMM-yyyy hh:MM:ss.fff}{1}".Put(toSend[0].Item1, body.IsEmpty() ? null : (": " + body));
                    SendEmailInternal(subject, message);
                } else {
                    var subject = "{0} robot messages".Put(toSend.Length);
                    var message = string.Join("\n#####\n", toSend.Select(t => "{0:dd-MMM-yyyy hh:MM:ss.fff}: {1}{2}".Put(t.Item1, t.Item2, (t.Item3.IsEmpty() ? null : (": " + t.Item3)))));
                    SendEmailInternal(subject, message);
                }
            } catch(Exception e) {
                _log.Dbg.AddErrorLog("error handler emailsender timer: {0}", e);
            }
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        bool SendEmailInternal(string subject, string text = null) {
            var fromAddress = new MailAddress("optionbot123@gmail.com");
            var toAddress = new MailAddress(_address);

            subject = "[OptionBot] " + subject;

            text = text ?? string.Empty;

            var smtp = new SmtpClient {
                Host = "smtp.gmail.com",
                Port = 587,
                EnableSsl = true,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false,
                Credentials = new NetworkCredential(fromAddress.Address, "i0JelvHAGLPt8f_x"),
                Timeout = 15000
            };

            _log.Dbg.AddInfoLog("sending email '{0}' to {1}...", subject, _address);

            try {
                using(var message = new MailMessage(fromAddress, toAddress) {Subject = subject, Body = text}) {
                    smtp.Send(message);
                }
            } catch(Exception ex) {
                _log.AddErrorLog("Unable to send email: {0}", ex);
                return false;
            }

            _log.Dbg.AddInfoLog("email was sent successfully");
            return true;
        }

        public static bool VerifyEmailAddress(string address) {
            if(String.IsNullOrEmpty(address)) return false;
            try {
                // ReSharper disable once ObjectCreationAsStatement
                new MailAddress(address);
                return true;
            } catch(Exception e) {
                _log.Dbg.AddWarningLog("incorrect email: {0}, {1}", address, e);
                return false;
            }
        }
    }
}

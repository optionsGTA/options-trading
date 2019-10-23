using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Management;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Threading;
using System.Xml;
using AsyncHandler;
using Ecng.Common;
using Ecng.Interop;
using OptionBot.Config;
using OptionBot.robot;
using StockSharp.Algo;
using StockSharp.BusinessEntities;
using StockSharp.Messages;
using Timer = System.Timers.Timer;

namespace OptionBot
{
    static class Util {
        static readonly Logger _log = new Logger("Util");

        public static readonly CultureInfo RuCulture = new CultureInfo("ru-RU");

        #region Market data helpers

        /// <summary>
        /// Проверить, находится ли заявка S# в своем конечном состоянии.
        /// </summary>
        /// <param name="order">Заявка S#.</param>
        /// <returns>True, если заявка находится в конечном состоянии.</returns>
        public static bool IsInFinalState(this Order order) {
            return order.State == OrderStates.Done || order.State == OrderStates.Failed;
        }

        public static bool IsFinalState(this OrderStates state) {
            return state == OrderStates.Done || state == OrderStates.Failed;
        }

        public static decimal GetMoneyPrice(this Security sec, decimal pointPrice) {
            return sec.StepPrice > 0 && sec.StepPrice != sec.PriceStep ?
                   pointPrice * sec.StepPrice / sec.PriceStep :
                   pointPrice;
        }

        public static decimal GetLotSize(this Security sec) {
            return sec.Type == SecurityTypes.Future ? 1 : sec.VolumeStep;
        }

        public static bool IsFuture(this SecurityTypes secType) {
            return secType == SecurityTypes.Future;
        }

        public static string FormatPriceString(this decimal price, int decimals, string format="N") {
            return string.Format(string.Format("{{0:{0}{1}}}", format, decimals), price);
        }

        public static DateTime GetExpirationTime(this Security security) {
            if(security.ExpiryDate == null) throw new ArgumentException("no expiration date found for '{0}'".Put(security.Code));

            var expDate = security.ExpiryDate.Value;
            return expDate.TimeOfDay == TimeSpan.Zero ? 
                        expDate + security.Board.ExpiryTime : expDate;
        }

        public static void GetQuotes(this MarketDepthPair pair, out decimal bidPrice, out decimal askPrice) {
            if(pair != null) {
                bidPrice = pair.Bid.Return(q => q.Price, 0);
                askPrice = pair.Ask.Return(q => q.Price, 0);
            } else {
                bidPrice = askPrice = 0;
            }
        }

        public static PricePair GetQuotes(this MarketDepthPair pair) {
            if(pair != null) {
                return new PricePair(pair.Bid.Return(q => q.Price, 0), pair.Ask.Return(q => q.Price, 0));
            }
            return new PricePair();
        }

        public static void GetQuotes(this MarketDepthPair pair, out decimal bidPrice, out decimal bidSize, out decimal askPrice, out decimal askSize) {
            if(pair != null) {
                if(pair.Bid != null) {
                    bidPrice = pair.Bid.Price;
                    bidSize = pair.Bid.Volume;
                } else {
                    bidPrice = bidSize = 0;
                }

                if(pair.Ask != null) {
                    askPrice = pair.Ask.Price;
                    askSize = pair.Ask.Volume;
                } else {
                    askPrice = askSize = 0;
                }
            } else {
                bidPrice = askPrice = bidSize = askSize = 0;
            }
        }

        public static bool QuoteVolPriceEqual(Quote q1, Quote q2) {
            if(q1 == null || q2 == null) return object.ReferenceEquals(q1, q2);
            return q1.Volume == q2.Volume && q1.Price == q2.Price;
        }

        public static bool CanCancelOrders(this ConnectionState state) {
            return state == ConnectionState.Connected || state == ConnectionState.Synchronizing;
        }

        public static decimal RoundStep(this decimal x, decimal step) {
            return Math.Round(x / step) * step;
        }

        public static decimal FloorStep(this decimal x, decimal step) {
            return Math.Floor(x / step) * step;
        }

        public static decimal CeilingStep(this decimal x, decimal step) {
            return Math.Ceiling(x / step) * step;
        }

        #region min/max

        public static DateTime Min(DateTime d1, DateTime d2) {
            return d1 <= d2 ? d1 : d2;
        }

        public static DateTime Max(DateTime d1, DateTime d2) {
            return d1 >= d2 ? d1 : d2;
        }

        // int
        public static int Min(int val1, int val2, int val3) { return Math.Min(val1, Math.Min(val2, val3)); }
        public static int Min(int val1, int val2, int val3, int val4) { return Math.Min(Math.Min(val1, val2), Math.Min(val3, val4)); }
        public static int Min(int val1, int val2, int val3, int val4, int val5) { return Math.Min(Math.Min(Math.Min(val1, val2), Math.Min(val3, val4)), val5); }

        public static int Max(int val1, int val2, int val3) { return Math.Max(val1, Math.Max(val2, val3)); }
        public static int Max(int val1, int val2, int val3, int val4) { return Math.Max(Math.Max(val1, val2), Math.Max(val3, val4)); }
        public static int Max(int val1, int val2, int val3, int val4, int val5) { return Math.Max(Math.Max(Math.Max(val1, val2), Math.Max(val3, val4)), val5); }

        // double
        public static double Min(double val1, double val2, double val3) { return Math.Min(val1, Math.Min(val2, val3)); }
        public static double Min(double val1, double val2, double val3, double val4) { return Math.Min(Math.Min(val1, val2), Math.Min(val3, val4)); }
        public static double Min(double val1, double val2, double val3, double val4, double val5) { return Math.Min(Math.Min(Math.Min(val1, val2), Math.Min(val3, val4)), val5); }

        public static double Max(double val1, double val2, double val3) { return Math.Max(val1, Math.Max(val2, val3)); }
        public static double Max(double val1, double val2, double val3, double val4) { return Math.Max(Math.Max(val1, val2), Math.Max(val3, val4)); }
        public static double Max(double val1, double val2, double val3, double val4, double val5) { return Math.Max(Math.Max(Math.Max(val1, val2), Math.Max(val3, val4)), val5); }

        // decimal
        public static decimal Min(decimal val1, decimal val2, decimal val3) { return Math.Min(val1, Math.Min(val2, val3)); }
        public static decimal Min(decimal val1, decimal val2, decimal val3, decimal val4) { return Math.Min(Math.Min(val1, val2), Math.Min(val3, val4)); }
        public static decimal Min(decimal val1, decimal val2, decimal val3, decimal val4, decimal val5) { return Math.Min(Math.Min(Math.Min(val1, val2), Math.Min(val3, val4)), val5); }

        public static decimal Max(decimal val1, decimal val2, decimal val3) { return Math.Max(val1, Math.Max(val2, val3)); }
        public static decimal Max(decimal val1, decimal val2, decimal val3, decimal val4) { return Math.Max(Math.Max(val1, val2), Math.Max(val3, val4)); }
        public static decimal Max(decimal val1, decimal val2, decimal val3, decimal val4, decimal val5) { return Math.Max(Math.Max(Math.Max(val1, val2), Math.Max(val3, val4)), val5); }

        #endregion

        public static bool IsZero(this double val) {
            return Math.Abs(val) < BaseConfig.EpsilonDouble;
        }

        public static bool IsEqual(this double val, double other) {
            return Math.Abs(val - other) < BaseConfig.EpsilonDouble;
        }

        public static bool IsMarketOpen(this MarketPeriodType period) {
            return period == MarketPeriodType.MorningSession || period == MarketPeriodType.MainSession || period == MarketPeriodType.EveningSession;
        }

        public static bool IsRobotActivePeriod(this RobotPeriodType period) {
            return period == RobotPeriodType.Active;
        }

        public static CanTradeState Normalize(this CanTradeState state) {
            if(state.HasFlag(CanTradeState.CanOpenPositions))
                state = CanTradeState.CanCalculate | CanTradeState.CanClosePositions | CanTradeState.CanOpenPositions;
            else if(state.HasFlag(CanTradeState.CanClosePositions))
                state = CanTradeState.CanCalculate | CanTradeState.CanClosePositions;

            return state;
        }

        #endregion

        public static V TryGetValue<K, V>(this ConditionalWeakTable<K, V> table, K key) where K:class where V:class {
            V result;
            table.TryGetValue(key, out result);
            return result;
        }

        public static string FormatError(string message, Exception ex) {
            return string.Format("{0}{1}{2}", message, message!=null&&ex!=null?" exception=":"", ex);
        }

        public static T Swap<T>(this T x, ref T y) {
            var tmp = y;
            y = x;
            return tmp;
        }

        public static decimal ToDecimalChecked(this double d) {
            try {
                return double.IsInfinity(d) ? 
                            (double.IsPositiveInfinity(d) ? decimal.MaxValue : decimal.MinValue) :
                            Convert.ToDecimal(d);
            } catch(Exception e) {
                var res =   d > (double)decimal.MaxValue ? decimal.MaxValue :
                            d < (double)decimal.MinValue ? decimal.MinValue :
                            0m;
                _log.Dbg.AddWarningLog("unable to convert {0} to decimal, returning {1}: {2}", d, res, e);

                return res;
            }
        }

        public static int ToInt32Checked(this decimal d) {
            try {
                return d > int.MaxValue ? int.MaxValue :
                       d < int.MinValue ? int.MinValue :
                       (int)d;
            } catch(Exception e) {
                var res =   d > int.MaxValue ? int.MaxValue :
                            d < int.MinValue ? int.MinValue :
                            0;
                _log.Dbg.AddWarningLog("unable to convert {0} to int, returning {1}: {2}", d, res, e);

                return res;
            }
        }

        public static int ToInt32Checked(this double d) {
            try {
                if(d.IsNaN()) throw new InvalidOperationException("ToInt32Checked: double is NaN");

                return d > int.MaxValue ? int.MaxValue :
                       d < int.MinValue ? int.MinValue :
                       (int)d;
            } catch(Exception e) {
                var res =   d > int.MaxValue ? int.MaxValue :
                            d < int.MinValue ? int.MinValue :
                            0;
                _log.Dbg.AddWarningLog("unable to convert {0} to int, returning {1}: {2}", d, res, e);

                return res;
            }
        }

        public static bool IsNaN(this double value) {
            // NOTE: Value != Value check is intentional
            // http://stackoverflow.com/questions/3286492/can-i-improve-the-double-isnan-x-function-call-on-embedded-c

            // ReSharper disable once EqualExpressionComparison
            // ReSharper disable CompareOfFloatsByEqualityOperator
            return value != value;
            // ReSharper restore CompareOfFloatsByEqualityOperator
        }

        static public IEnumerable<string> GetInterfaceProperties(this Type t) {
            if(!t.IsInterface) throw new InvalidOperationException($"{t.Name} is not interface");

            return t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name);
        } 

        public static string ToGenericTypeString(this Type t) {
            if(!t.IsGenericType)
                return t.Name;

            var genericTypeName = t.GetGenericTypeDefinition().Name;
            genericTypeName = genericTypeName.Substring(0, genericTypeName.IndexOf('`'));

            var genericArgs = string.Join(",", t.GetGenericArguments().Select(ToGenericTypeString).ToArray());
            return genericTypeName + "<" + genericArgs + ">";
        }

        public static string PropertyName<T>(Expression<Func<T>> property) {
            var lambda = (LambdaExpression)property;

            MemberExpression memberExpression;
            if(lambda.Body is UnaryExpression) {
                var unaryExpression = (UnaryExpression)lambda.Body;
                memberExpression = (MemberExpression)unaryExpression.Operand;
            } else {
                memberExpression = (MemberExpression)lambda.Body;
            }

            return memberExpression.Member.Name;
        }

        public static TimeSpan GetDistanceFromTimeRange(DateTime time, DateTime min, DateTime max) {
            if(time < min) return time - min;
            if(time > max) return time - max;
            return TimeSpan.Zero;
        }

        public static ListSortDirection Invert(this ListSortDirection val) {
            return val == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        }

        public static bool CanTrade(this CanTradeState state) {
            return state.HasFlag(CanTradeState.CanClosePositions);
        }

        public static bool CanOpenPositions(this CanTradeState state) {
            return state.HasFlag(CanTradeState.CanOpenPositions);
        }

        public static bool CanCalculate(this CanTradeState state) {
            return state.HasFlag(CanTradeState.CanCalculate);
        }

        public static bool Inactive(this StrategyState state) {
            return state == StrategyState.Inactive || state == StrategyState.Failed;
        }

        public static bool CanStop(this StrategyState state) {
            return state == StrategyState.Active || state == StrategyState.Starting;
        }

        public static string GetFileSha1(string filename) {
            try {
                using(var fs = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using(var bs = new BufferedStream(fs))
                using(var sha1 = new SHA1Managed()) {
                    var hash = sha1.ComputeHash(bs);
                    var formatted = new StringBuilder(2*hash.Length);
                    foreach(var b in hash)
                        formatted.AppendFormat("{0:x2}", b);

                    return formatted.ToString();
                }
            } catch(Exception e) {
                _log.Dbg.AddWarningLog("unable to get SHA1 for {0}: {1}", filename, e);
                return null;
            }
        }

        public static void CancelDelayedAction(ref HTCancellationToken cancelToken) {
            var t = cancelToken;
            if(t != null) {
                t.Cancel();
                cancelToken = null;
            }
        }

        public static void CancelDelayedAction(ref ICancellationToken cancelToken) {
            var t = cancelToken;
            if(t != null) {
                t.Cancel();
                cancelToken = null;
            }
        }

        #region xml load/save

        public static T LoadFromXml<T>(this string filename) {
            if(filename.IsEmpty())
                throw new ArgumentException("filename");

            try {
                T obj;
                using(var reader = XmlReader.Create(filename)) {
                    var serializer = CreateSerializer(typeof(T));
                    obj = (T)serializer.ReadObject(reader);
                }

                _log.AddInfoLog("Загружен объект {0} из файла {1}", obj.GetType().Name, filename);

                return obj;
            } catch(Exception e) {
                _log.AddErrorLog("Невозможно загрузить объект {0} из файла {1}: {2}", typeof(T).Name, filename, e);
                return default(T);
            }
        }

        public static IEnumerable<T> LoadListFromXml<T>(this string filename, bool newOnFail = true) {
            if(filename.IsEmpty())
                throw new ArgumentException("filename");

            try {
                List<T> list;
                using(var reader = XmlReader.Create(filename)) {
                    var serializer = CreateSerializer(typeof(List<T>));
                    list = (List<T>)serializer.ReadObject(reader);
                }

                _log.AddInfoLog("Загружено {0} объектов {1} из файла {2}", list.Count, typeof(T).Name, filename);

                return list;
            } catch(Exception e) {
                _log.AddErrorLog("Невозможно загрузить список объектов {0} из файла {1}: {2}", typeof(T).Name, filename, e);
                return newOnFail ? new List<T>() : null;
            }
        }

        public static void SaveToXml<T>(this T obj, string filename, Type serializerType = null) {
            EnsureSaveFileName(filename);

            try {
                var settings = new XmlWriterSettings { Indent = true };

                using(var writer = XmlWriter.Create(filename, settings)) {
                    var serializer = CreateSerializer(serializerType ?? obj.GetType());
                    serializer.WriteObject(writer, obj);
                }

                _log.AddInfoLog("Объект {0} сохранен в файл {1}", obj.GetType().Name, filename);
            } catch(Exception e) {
                _log.AddErrorLog("Сохранение конфигурации {0} завершилось с ошибкой: {1}", obj.GetType().Name, e);
            }
        }

        public static void SaveListToXml<T>(this IEnumerable<T> obj, string filename) {
            EnsureSaveFileName(filename);

            try {
                var list = obj.ToList();

                var settings = new XmlWriterSettings { Indent = true };

                using(var writer = XmlWriter.Create(filename, settings)) {
                    var serializer = CreateSerializer(typeof(List<T>));
                    serializer.WriteObject(writer, list);
                }

                _log.AddInfoLog("{0} объектов {1} сохранено в файл {2}", list.Count, typeof(T).Name, filename);
            } catch(Exception e) {
                _log.AddErrorLog("Сохранение списка объектов {0} завершилось с ошибкой: {1}", obj.GetType().Name, e);
            }
        }

        static DataContractSerializer CreateSerializer(Type t) {
            return new DataContractSerializer(t, null, 
                                    300000  /*maxItemsInObjectGraph*/, 
                                    false   /*ignoreExtensionDataObject*/, 
                                    true    /*preserveObjectReferences */, 
                                    null    /*dataContractSurrogate*/);
        }

        static void EnsureSaveFileName(string filename) {
            if(filename.IsEmpty())
                throw new ArgumentException("filename");

            var dir = Path.GetDirectoryName(filename);
            if(!dir.IsEmpty())
                (dir + Path.DirectorySeparatorChar).CreateDirIfNotExists();
        }

        #endregion

        #region threading helpers

        public static void CheckThread(this Dispatcher dispatcher, [CallerMemberName] string name = null) {
            if(!dispatcher.CheckAccess())
                _log.Dbg.AddErrorLog("{0}: unexpected thread (dispatcher)", name);
        }

        public static void MyGuiAsync(this Dispatcher dispatcher, Action action, bool forceAsync = false, DispatcherPriority priority = DispatcherPriority.Normal) {
            if(!forceAsync && dispatcher.CheckAccess())
                action();
            else
                dispatcher.BeginInvoke(action, priority);
        }

        public static bool InThread<T>(this HandlerThread<T> thread) where T : struct, IComparable, IFormattable, IConvertible {
            return Thread.CurrentThread.ManagedThreadId == thread.ManagedThreadId;
        }

        public static void CheckThread<T>(this HandlerThread<T> thread, [CallerMemberName] string name = null) where T : struct, IComparable, IFormattable, IConvertible {
            if(!thread.InThread())
                _log.Dbg.AddErrorLog("{0}: unexpected thread", name);
        }

        public static void ExecuteSync<T>(this HandlerThread<T> thread, Action action, object tag = null) where T : struct, IComparable, IFormattable, IConvertible {
            if(!thread.IsAlive) {
                _log.Dbg.AddErrorLog("thread '{0}' is dead.", thread.Name);
                return;
            }

            thread.Send(action, tag);
        }

        public static void ExecuteSyncCheck<T>(this HandlerThread<T> thread, Action action, object tag = null) where T : struct, IComparable, IFormattable, IConvertible {
            try {
                thread.ExecuteSync(action, tag);
            } catch(Exception e) {
                _log.AddErrorLog("{0}: Исключение в потоке {1}: {2}", Util.GetCallingTypeName(), thread.Name, e);
            }
        }

        public static void ExecuteAsync<T>(this HandlerThread<T> thread, Action action, bool forceAsync) where T : struct, IComparable, IFormattable, IConvertible {
            thread.ExecuteAsync(action, null, null, forceAsync);
        }

        public static void ExecuteAsync<T>(this HandlerThread<T> thread, Action action, T priority) where T : struct, IComparable, IFormattable, IConvertible {
            thread.ExecuteAsync(action, null, priority);
        }

        public static void ExecuteForceAsync<T>(this HandlerThread<T> thread, Action action) where T : struct, IComparable, IFormattable, IConvertible {
            if(!thread.IsAlive) { _log.Dbg.AddErrorLog("thread '{0}' is dead.", thread.Name); return; }

            thread.Post(() => {
                try {
                    action();
                } catch(Exception ex) {
                    _log.AddErrorLog("Исключение в потоке {0}: {1}", thread.Name, ex);
                }
            }, null, thread.DefaultPriority);
        }

        public static void ExecuteAsync<T>(this HandlerThread<T> thread, Action action, object tag = null, T? priority = null, bool forceAsync = false) where T : struct, IComparable, IFormattable, IConvertible {
            if(thread.InThread() && !forceAsync) { thread.ExecuteSyncCheck(action, tag); return; }
            if(!thread.IsAlive) {
                _log.Dbg.AddErrorLog("thread '{0}' is dead.", thread.Name);
                return;
            }

            thread.Post(() => {
                try {
                    action();
                } catch(Exception ex) {
                    _log.AddErrorLog("Исключение в потоке {0}: {1}", thread.Name, ex);
                }
            }, tag, priority ?? thread.DefaultPriority);
        }

        public static void ExecuteAsync(this Security sec, Action action, string tag) {
            var c = sec.Connector as PlazaTraderEx;
            if(c == null) throw new ArgumentNullException("sec.Connector");

            var processor = c.GetSecurityProcessor(sec);
            if(processor == null) {
                _log.Dbg.AddErrorLog("unable to post action for {0}", sec.Id);
                return;
            }

            processor.Post(action, tag);
        }

        public static void CheckThread(this ISecurityProcessor processor, [CallerMemberName] string name = null) {
            if(!processor.IsInSecurityThread)
                _log.Dbg.AddErrorLog("{0}: unexpected thread. CurrentSecurityThreadId={1}", name, processor.CurrentSecurityThreadId);
        }

        public static Timer When<T>(this HandlerThread<T> thread, int checkPeriodMs, int timeoutSec, 
                                    Func<bool> condition, Func<bool> cancelCondition, Action actionDone) where T : struct, IComparable, IFormattable, IConvertible {
            if(thread == null || !thread.IsAlive) {
                _log.Dbg.AddWarningLog("When(): thread is dead");
                return null;
            }

            if(cancelCondition!=null && cancelCondition()) return null;
            if(condition()) {thread.Post(actionDone);}

            var startedAt = DateTime.Now;
            var timer = new Timer(checkPeriodMs);
            timer.Elapsed += (sender, args) => {
                lock(timer) {
                    if(!timer.Enabled)
                        return;
                    timer.Stop();
                }

                if(!thread.IsAlive) {
                    _log.Dbg.AddWarningLog("When(): the thread had died. cancelling...");
                    return;
                }

                if(cancelCondition != null && cancelCondition())
                    return;

                if(condition()) {
                    thread.Post(actionDone);
                    return;
                }

                if((DateTime.Now - startedAt).TotalSeconds >= timeoutSec) {
                    thread.Post(actionDone);
                } else {
                    timer.Start();
                }
            };
            timer.Start();
            return timer;
        }

        #endregion

        public static string GetRecalcReasonDescription(this RecalcReason reason) {
            switch(reason) {
                case RecalcReason.FutureChanged:
                    return "future changed";
                case RecalcReason.OptionChanged:
                    return "option changed";
                case RecalcReason.FutureSettingsUpdated:
                    return "future settings updated";
                case RecalcReason.GeneralSettingsUpdated:
                    return "general settings updated";
                case RecalcReason.VPSettingsUpdated:
                    return "vp settings updated";
                case RecalcReason.CanTradeStateChanged:
                    return "can trade state changed";
                case RecalcReason.ATMStrikeChanged:
                    return "ATM strike changed";
                case RecalcReason.FutureRecalculatedOnPositionChange:
                    return "future params recalculated on pos change";
                case RecalcReason.OrderOrPositionUpdate:
                    return "order or position changed";
                case RecalcReason.RealtimeMode:
                    return "realtime mode";
                case RecalcReason.CanCalculateMode:
                    return "cancalc mode";
                case RecalcReason.ForcedRecalculate:
                    return "forced recalculate";
                case RecalcReason.TranRateControllerStateChanged:
                    return "transactions rate ctl state changed";
                case RecalcReason.MoneyErrorDelay:
                    return "money error delay";
                case RecalcReason.OnStart:
                    return "strategy started";
            }

            return reason.ToString();
        }

        public static string GetCallingTypeName(int depth=1) {
            var t = new StackFrame(depth+1, false).GetMethod().DeclaringType;
            return t != null ? t.Name : "unknown";
        }

        public static T CreateInitializable<T>(Func<T> creator) where T:IInitializable {
            var obj = creator();
            obj.Init();

            return obj;
        }

        public static void SortStable<T>(this List<T> list, Comparison<T> comp = null) {
            var listStableOrdered = comp == null ?
                list.OrderBy(x => x).ToList() :
                list.OrderBy(x => x, Comparer<T>.Create(comp)).ToList();
            list.Clear();
            list.AddRange(listStableOrdered);
        }

        public static string[] CopyProperties(object o1, object o2, string[] names) {
            var failedNames = new List<string>();
            if(o1 == null) { _log.Dbg.AddWarningLog("CopyProperties: o1==null"); return failedNames.ToArray(); }
            if(o2 == null) { _log.Dbg.AddWarningLog("CopyProperties: o2==null"); return failedNames.ToArray(); }
            if(names == null || names.Length == 0) { _log.Dbg.AddWarningLog("CopyProperties: nothing to copy"); return failedNames.ToArray(); }

            var t1 = o1.GetType();
            var t1props = t1.GetProperties();
            var t2 = o2.GetType();
            var t2props = t2.GetProperties();

            foreach(var n in names) {
                var p1 = t1props.FirstOrDefault(p => p.Name == n);
                var p2 = t2props.FirstOrDefault(p => p.Name == n);

                if(p1 == null) {_log.Dbg.AddWarningLog($"t1={t1.Name}: no property '{n}'"); failedNames.Add(n); continue; }
                if(p2 == null) {_log.Dbg.AddWarningLog($"t2={t2.Name}: no property '{n}'"); failedNames.Add(n); continue; }

                if(p1.PropertyType != p2.PropertyType) {_log.Dbg.AddWarningLog($"type1 != type2: {p1.PropertyType.Name} != {p2.PropertyType.Name}"); }

                if(!p2.CanWrite) {_log.Dbg.AddWarningLog($"property {t2.Name}.{n} is not writable"); failedNames.Add(n); continue; }

                try {
                    p2.SetValue(o2, p1.GetValue(o1));
                } catch(Exception e) {
                    _log.Dbg.AddWarningLog($"Unable to set property {n}: {e}");
                    failedNames.Add(n);
                }
            }

            return failedNames.ToArray();
        }

        public static int MySign(this double val) => val < 0 ? -1 : 1;

        public static string FormatInterval(this TimeSpan delay) {
            return delay == TimeSpan.Zero ? string.Empty : $"{(int)delay.TotalHours,2:00}:{delay.Minutes,2:00}:{delay.Seconds,2:00}";
        }

        public static void ClearOneByOne<T>(this IList<T> lst) {
            while(lst.Count > 0)
                lst.RemoveAt(lst.Count - 1);
        }
    }

    public struct PricePair {
        readonly decimal _bid, _ask;
        public decimal Bid {get {return _bid;}}
        public decimal Ask {get {return _ask;}}

        public PricePair(decimal bid, decimal ask) {
            _bid = bid; _ask = ask;
        }
    }

    public static class ImmediateSearch<T> {
        public static IEnumerable<T> SearchCollection(IEnumerable<T> source, string propertyPath, dynamic requiredValue) {
            var result = new List<T>();
            foreach(var elem in source)
                if(ExtractValueWithPath(elem, propertyPath) == requiredValue)
                    result.Add(elem);

            return result;
        }

        private static dynamic ExtractValueWithPath(dynamic element, string propertyPath) {
            if(element == null)
                return element;

            List<string> propertyPathComponents = propertyPath.Split('.').ToList();

            var propValue = element.GetType().GetProperty(propertyPathComponents.First()).GetValue(element, null);
            propertyPathComponents.RemoveAt(0);

            if(propertyPathComponents.Count != 0)
                propValue = ExtractValueWithPath(propValue, String.Join(".", propertyPathComponents));

            return propValue;
        }
    }

    public class NoResetObservableCollection<T> : ObservableCollection<T> {
        /// <summary>Clears all items in the collection by removing them individually.</summary>
        protected sealed override void ClearItems() {
            var items = new List<T>(this);
            foreach(var item in items)
                Remove(item);
        }
    }

    public static class SteadyClock {
        static readonly DateTime _start = DateTime.UtcNow;
        static readonly Stopwatch _watch = Stopwatch.StartNew();
        public static DateTime Now => _start + _watch.Elapsed;
    }

    public static class Maybe {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TResult With2<TInput, TResult>(this TInput? o, Func<TInput, TResult> eval) where TInput:struct where TResult:class {
            return o!=null ? eval(o.Value) : null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TResult With<TInput, TResult>(this TInput o, Func<TInput, TResult> eval) where TInput:class where TResult:class {
            return o!=null ? eval(o) : null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TResult Return<TInput, TResult>(this TInput o, Func<TInput, TResult> eval, TResult failureValue) where TInput:class {
            return o!=null ? eval(o) : failureValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TResult Return2<TInput, TResult>(this TInput? o, Func<TInput, TResult> eval, TResult failureValue) where TInput:struct {
            return o!=null ? eval(o.Value) : failureValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TInput If<TInput>(this TInput o, Func<TInput, bool> eval) where TInput:class {
            return (o!=null && eval(o)) ? o : null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TInput Unless<TInput>(this TInput o, Func<TInput, bool> eval) where TInput:class {
            return (o!=null && !eval(o)) ? o : null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TInput Do<TInput>(this TInput o, Action<TInput> action) where TInput:class {
            if(o!=null) action(o);
            return o;
        }
    }

    public class TupleList<T1, T2> : List<Tuple<T1, T2>> {
        public void Add(T1 item, T2 item2) {
            Add(new Tuple<T1, T2>(item, item2));
        }
    }

    public class DeferredUIAction {
        readonly Action _action;
        readonly Dispatcher _dispatcher;
        TimeSpan _delay;

        DispatcherTimer _timer;

        public DeferredUIAction(Dispatcher dispatcher, Action action, TimeSpan delay) {
            _dispatcher = dispatcher;
            _action = action;
            _delay = delay;
        }

        public void DeferredExecute(TimeSpan newDelay = default(TimeSpan)) {
            if(newDelay != default(TimeSpan))
                _delay = newDelay;

            _dispatcher.MyGuiAsync(() => {
                if(_timer != null) {
                    _timer.Stop();
                    _timer = null;
                }

                _timer = new DispatcherTimer { Interval = _delay };
                _timer.Tick += timer_Tick;
                _timer.Start();
            });
        }

        public void Cancel() {
            _dispatcher.MyGuiAsync(() => {
                if(_timer != null) {
                    _timer.Stop();
                    _timer = null;
                }
            });
        }

        void timer_Tick(object sender, EventArgs e) {
            _timer.Stop();
            _timer = null;

            _action();
        }
    }

//    public class PerformanceAnalyzer {
//        readonly Stopwatch _watch = new Stopwatch();
//        readonly List<Tuple<int, TimeSpan, string>> _messages = new List<Tuple<int, TimeSpan, string>>(15);
//
//        public void Restart() {
//            _messages.Clear();
//            _watch.Restart();
//        }
//
//        public void Checkpoint(string msg) {
//            _messages.Add(Tuple.Create(Thread.CurrentThread.ManagedThreadId, _watch.Elapsed, msg));
//        }
//
//        public void Stop(string msg = null) {
//            _watch.Stop();
//            if(msg != null) Checkpoint(msg);
//        }
//
//        public IEnumerable<string> Report() {
//            return _messages.Select(t => "{0} - {1:0.###}ms - {2}".Put(t.Item1, t.Item2.TotalMilliseconds, t.Item3));
//        } 
//
//        public string Report(string separator) {
//            return string.Join(separator, Report());
//        } 
//    }

    public static class PerformanceInfo {
        const int _mb = 1024*1024;

        static readonly PerformanceCounter _cpuCounter;
        static readonly PerformanceCounter _ramCounter;
        static readonly PerformanceCounter _workingSet;

        public static int PhysicalProcessorCount {get; private set;}
        public static int ProcessorCoreCount {get; private set;}
        public static int LogicalProcessorCount {get; private set;}

        static PerformanceInfo() {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _ramCounter = new PerformanceCounter("Memory", "Available MBytes");

            var name = Process.GetCurrentProcess().ProcessName;
            _workingSet = new PerformanceCounter("Process", "Working Set - Private", name);

            foreach (var item in new ManagementObjectSearcher("Select * from Win32_ComputerSystem").Get()) {
                PhysicalProcessorCount = int.Parse(item["NumberOfProcessors"].ToString());
            }

            ProcessorCoreCount = new ManagementObjectSearcher("Select * from Win32_Processor")
                .Get().Cast<ManagementBaseObject>()
                .Sum(item => int.Parse(item["NumberOfCores"].ToString()));

            LogicalProcessorCount = Environment.ProcessorCount;
        }

        public static string GetCurrentCpuUsage(){
            return _cpuCounter.NextValue()+"%";
        }

        public static string GetAvailableRAM(){
            return _ramCounter.NextValue()+"MB";
        }

        public static string GetWorkingSet() {
            return _workingSet.NextValue()/_mb + "MB";
        }

        public static string GetTotalRam() {
            try {
                var searcher = new ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                foreach (var o in searcher.Get()) {
                    var WniPART = (ManagementObject)o;
                    var sizeMB = Convert.ToUInt64(WniPART.Properties["TotalPhysicalMemory"].Value) / _mb;
                    return "{0}MB".Put(sizeMB);
                }
            } catch(Exception e) {
                return "error ({0})".Put(e.Message);
            }

            return "error";
        }
    }
}

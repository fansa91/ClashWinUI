using ClashDotNetFramework.Models.Enums;
using ClashDotNetFramework.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Timer = System.Timers.Timer;

namespace ClashDotNetFramework.Controllers
{
    public abstract class Guard
    {
        /// <summary>
        /// 保存Buffer计时器
        /// </summary>
        private static readonly Timer SaveBufferTimer = new Timer(300) { AutoReset = true };

        /// <summary>
        /// 日志Buffer
        /// </summary>
        private readonly StringBuilder _logBuffer = new StringBuilder();

        /// <summary>
        /// 日志文件(重定向输出文件)
        /// </summary>
        private string _logPath;

        /// <summary>
        /// 成功启动关键词
        /// </summary>
        protected virtual IEnumerable<string> StartedKeywords { get; } = null;

        /// <summary>
        /// 启动失败关键词
        /// </summary>
        protected virtual IEnumerable<string> StoppedKeywords { get; } = null;

        /// <summary>
        /// 主程序名称
        /// </summary>
        public virtual string Name { get; }

        /// <summary>
        /// 主程序文件名
        /// </summary>
        public virtual string MainFile { get; protected set; }

        /// <summary>
        /// 实例状态
        /// </summary>
        protected State State { get; set; } = State.Waiting;

        /// <summary>
        /// 重定向输出
        /// </summary>
        protected bool RedirectStd { get; set; } = false;

        /// <summary>
        /// 以管理员权限启动 (TUN 模式需要)
        /// </summary>
        protected bool Elevated { get; set; } = false;

        /// <summary>
        /// 程序输出的编码
        /// </summary>
        protected Encoding InstanceOutputEncoding { get; set; } = Encoding.GetEncoding("utf-8");

        /// <summary>
        /// 进程实例
        /// </summary>
        public Process Instance { get; private set; }

        /// <summary>
        /// 实例是否正在运行
        /// </summary>
        public bool IsRunning => State == State.Started;

        /// <summary>
        /// 停止进程
        /// </summary>
        public abstract void Stop();

        /// <summary>
        /// 初始化实例
        /// </summary>
        /// <param name="argument">进程参数</param>
        protected virtual void InitInstance(string argument)
        {
            bool redirect = RedirectStd && !Elevated;
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Global.ClashDotNetFrameworkDir, "bin", MainFile),
                WorkingDirectory = Path.Combine(Global.ClashDotNetFrameworkDir, "bin"),
                Arguments = argument,
                CreateNoWindow = true,
                UseShellExecute = !redirect,
                RedirectStandardOutput = redirect,
                StandardOutputEncoding = redirect ? InstanceOutputEncoding : null,
                RedirectStandardError = redirect,
                StandardErrorEncoding = redirect ? InstanceOutputEncoding : null,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            if (Elevated)
                startInfo.Verb = "runas";
            Instance = new Process { StartInfo = startInfo };
        }

        /// <summary>
        /// 停止实例
        /// </summary>
        protected void StopInstance()
        {
            try
            {
                if (Instance == null || Instance.HasExited)
                    return;

                Instance.Kill();
                Instance.WaitForExit();
            }
            catch (Win32Exception e)
            {
                Logging.Error($"停止 {MainFile} 错误：\n" + e);
            }
            catch
            {
            }
        }

        /// <summary>
        /// 启动实例
        /// </summary>
        /// <param name="argument">进程参数</param>
        /// <param name="priority">进程优先级</param>
        protected void StartInstanceAuto(string argument, ProcessPriorityClass priority = ProcessPriorityClass.Normal)
        {
            bool redirect = RedirectStd && !Elevated;
            InitInstance(argument);
            Instance.EnableRaisingEvents = true;
            if (redirect)
            {
                // 清理日志
                _logPath = Path.Combine(Utils.Utils.GetClashLogsDir(), $"{Name}.log");
                if (File.Exists(_logPath))
                    File.Delete(_logPath);

                Instance.OutputDataReceived += OnOutputDataReceived;
                Instance.ErrorDataReceived += OnOutputDataReceived;
            }

            Instance.Exited += OnExited;

            try
            {
                Instance.Start();
            }
            catch (Win32Exception e)
            {
                // 用户取消 UAC 提权或启动失败
                Logging.Error($"启动 {MainFile} 失败：\n" + e.Message);
                State = State.Stopped;
                return;
            }

            if (priority != ProcessPriorityClass.Normal)
            {
                Instance.PriorityClass = priority;
            }

            if (!redirect)
            {
                State = State.Started;
                return;
            }

            Instance.BeginOutputReadLine();
            Instance.BeginErrorReadLine();
            SaveBufferTimer.Elapsed += SaveBufferTimerEvent;
            SaveBufferTimer.Enabled = true;
            if (!(StartedKeywords?.Any() ?? false))
            {
                State = State.Started;
                return;
            }
        }

        /// <summary>
        /// 退出时执行的操作
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void OnExited(object sender, EventArgs e)
        {
            if (RedirectStd)
                SaveBufferTimer.Enabled = false;

            SaveBufferTimerEvent(null, null);

            State = State.Stopped;
        }

        /// <summary>
        /// 写入日志文件缓冲
        /// </summary>
        /// <param name="info"></param>
        /// <returns>转码后的字符串</returns>
        private void Write(string info)
        {
            _logBuffer.Append(info + Global.EOF);

        }
        /// <summary>
        /// 接收输出数据
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">数据</param>
        protected void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            // 程序结束, 接收到 null
            if (e.Data == null)
                return;

            Write(e.Data);
            // 检查启动
            if (State == State.Starting)
            {
                if (StartedKeywords.Any(s => e.Data.Contains(s)))
                    State = State.Started;
                else if (StoppedKeywords.Any(s => e.Data.Contains(s)))
                    State = State.Stopped;
            }
        }

        /// <summary>
        /// 计时器存储日志
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">数据</param>
        private void SaveBufferTimerEvent(object sender, EventArgs e)
        {
            try
            {
                if (_logPath != null && _logBuffer != null)
                {
                    File.AppendAllText(_logPath, _logBuffer.ToString());
                    _logBuffer.Clear();
                }
            }
            catch (Exception exception)
            {
                Logging.Warning($"写入 {Name} 日志错误：\n" + exception.Message);
            }
        }
    }
}

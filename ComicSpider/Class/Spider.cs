﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using ComicSpider;
using ComicSpider.App_dataTableAdapters;
using System.Linq;
using Jint;
using System.Text;

namespace ys.Web
{
	class Comic_spider
	{
		public Comic_spider()
		{
			stopped = true;

			file_queue = new Queue<Web_src_info>();
			volume_queue = new Queue<Web_src_info>();
			file_queue_lock = new object();
			volume_queue_lock = new object();
			thread_list = new List<Thread>();
		}

		public void Async_show_volume_list()
		{
			Load_script();

			Thread thread = new Thread(new ParameterizedThreadStart(Show_volume_list));
			thread.Name = "Show_volume_list";
			thread.Start(MainWindow.Main.Settings.Main_url);

			Report("Show volume list...");
		}
		public void Async_start(System.Windows.Controls.ItemCollection vol_info_list)
		{
			stopped = false;

			Load_script();

			foreach (Web_src_info vol_info in vol_info_list)
			{
				lock (volume_queue_lock)
				{
					volume_queue.Enqueue(vol_info); 
				}
			}

			for (int i = 0; i < int.Parse(MainWindow.Main.Settings.Thread_count); i++)
			{
				Thread downloader = new Thread(new ThreadStart(Downloader));
				downloader.Name = "Downloader" + i;
				downloader.Start();
				thread_list.Add(downloader);

				Thread info_getter = new Thread(new ThreadStart(Get_page_list));
				info_getter.Name = "Get_page_list" + i;
				info_getter.Start();
				thread_list.Add(info_getter);
			}

			Report("Comic Spider start...");
		}

		public void Stop(bool completed = false)
		{
			stopped = true;
			volume_queue.Clear();
			file_queue.Clear();

			if (!completed)
			{
				foreach (Thread thread in thread_list)
				{
					thread.Abort();
				}
			}
			thread_list.Clear();
		}
		public bool Stopped { get { return stopped; } }

		private bool stopped;
		private Queue<Web_src_info> file_queue;
		private Queue<Web_src_info> volume_queue;
		private object file_queue_lock;
		private object volume_queue_lock;
		private List<Thread> thread_list;
		private string script;

		private void Show_volume_list(object arg)
		{
			string url = arg as string;
			List<Web_src_info> vol_info_list = Get_volume_list(new Web_src_info(url, 0, ""));

			MainWindow.Main.Dispatcher.Invoke(
				new MainWindow.Show_vol_list_delegate(MainWindow.Main.Show_volume_list),
				vol_info_list);
		}
		private void Log_error(Exception ex, string url = "")
		{
			if (ex is ArgumentOutOfRangeException)
				throw ex;
			Report(ex.Message);

			try
			{
				Error_logTableAdapter a = new Error_logTableAdapter();
				a.Connection.Open();
				a.Insert(
					DateTime.Now,
					url,
					ex.Message,
					ex.StackTrace);
				a.Connection.Close();
			}
			catch (Exception)
			{
			}
		}
		private void Report(object info)
		{
			Console.WriteLine(info);
			MainWindow.Main.Dispatcher.Invoke(new MainWindow.Report_progress_delegate(MainWindow.Main.Report_progress), info);
		}
		private void Report(string format, params object[] arg)
		{
			string info = string.Format(format, arg);
			Console.WriteLine(info);
			MainWindow.Main.Dispatcher.Invoke(new MainWindow.Report_progress_delegate(MainWindow.Main.Report_progress), info);
		}

		private void Load_script()
		{
			var sr = new StreamReader(@"comic_spider.js");
			script = sr.ReadToEnd();
			sr.Close();
		}
		private List<Web_src_info> Get_volume_list(Web_src_info comic_info)
		{
			List<Web_src_info> vol_info_list = new List<Web_src_info>();

			vol_info_list = Get_info_list_from_html(comic_info, "get_comic_name", "get_volume_list");

			Report("Get volume list: {0}, Count: {1}", comic_info.Name, comic_info.Children == null ? 0 : comic_info.Children.Count);

			return vol_info_list;
		}
		private void Get_page_list()
		{
			while (!stopped)
			{
				Web_src_info vol_info;
				lock (volume_queue_lock)
				{
					if (volume_queue.Count == 0)
					{
						return;
					}
					vol_info = volume_queue.Dequeue();
				}

				if (vol_info.Children == null ||
					vol_info.Children.Count == 0)
				{
					try
					{
						vol_info.Children = Get_info_list_from_html(vol_info, "get_page_list");
					}
					catch (Exception ex)
					{
						if (ex is ThreadAbortException) return;

						Log_error(ex, vol_info.Url);
						continue;
					}
				}

				Report("Page list: {0}", vol_info.Name);

				Get_file_list(vol_info.Children);
			}
		}
		private void Get_file_list(List<Web_src_info> page_info_list)
		{
			string dir_path = "";

			if (page_info_list.Count == 0)
			{
				Report("No page found.");
				return;
			}

			#region Create folder
			Web_src_info parent = page_info_list[0];
			while ((parent = parent.Parent) != null)
			{
				dir_path = Path.Combine(parent.Name, dir_path);
			}
			dir_path = Path.Combine(MainWindow.Main.Settings.Root_dir, dir_path);
			if (!Directory.Exists(dir_path))
			{
				Directory.CreateDirectory(dir_path);
				Report("Create dir: {0}", dir_path);
			}
			#endregion

			foreach (var page_info in page_info_list)
			{
				if (stopped)
					return;
				if (page_info.State == Web_src_info.State_downloaded)
					continue;

				try
				{
					List<Web_src_info> file_info_list = Get_info_list_from_html(page_info, "get_file_list");

					lock (file_queue_lock)
					{
						file_queue.Enqueue(file_info_list[0]);
					}
					Report("Get file info: {0}", file_info_list[0].Url);
				}
				catch (Exception ex)
				{
					if (ex is ThreadAbortException) return;
					page_info.State = Web_src_info.State_missed;
					Log_error(ex, page_info.Url);
				}
			}
		}

		private void Downloader()
		{
			Web_src_info file_info;

			while (!stopped)
			{
				lock (file_queue_lock)
				{
					if (file_queue.Count == 0)
					{
						Thread.Sleep(100);
						continue;
					}

					file_info = file_queue.Dequeue();
				}

				#region Create file name

				string file_path = "";
				Web_src_info parent = file_info.Parent;
				while ((parent = parent.Parent) != null)
				{
					file_path = Path.Combine(parent.Name, file_path);
				}
				file_path = Path.Combine(file_path,
					string.Format("{0:D3}{1}", file_info.Parent.Index, Path.GetExtension(file_info.Url))
				);
				file_path = Path.Combine(MainWindow.Main.Settings.Root_dir, file_path);

				#endregion

				WebClient wc = new WebClient();
				wc.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 6.1; rv:10.0.2) Gecko/20100101 Firefox/10.0.2");
				wc.Headers.Add("Cookie", file_info.Parent.Cookie);
				wc.Headers.Add("Referer", file_info.Parent.Url);
				try
				{
					wc.DownloadFile(file_info.Url, file_path);
					byte[] data = wc.DownloadData(file_info.Url);

					FileStream sw = new FileStream(file_path, FileMode.Create);
					sw.Write(data, 0, data.Length);
					sw.Close();

					file_info.Parent.State = Web_src_info.State_downloaded;

					int downloaded = file_info.Parent.Parent.Downloaded;
					if (downloaded == file_info.Parent.Parent.Count)
					{
						file_info.Parent.Parent.State = Web_src_info.State_downloaded;
					}
					else
					{
						file_info.Parent.Parent.State = string.Format("{0}/{1}", downloaded, file_info.Parent.Parent.Count);
					}

					Report("{0}: {1}/{2} , Downloaded: {3}",
						file_info.Parent.Parent.Name,
						downloaded,
						file_info.Parent.Parent.Count,
						file_info.Name);
				}
				catch (Exception ex)
				{
					if (ex is ThreadAbortException) return;

					file_info.Parent.State = Web_src_info.State_missed;
					Log_error(ex, file_info.Url);
				}
				finally
				{
					MainWindow.Main.Dispatcher.Invoke(
						new MainWindow.Report_download_progress_delegate(
							MainWindow.Main.Report_download_progress
						)
					);
				}
			}
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="src_info"></param>
		/// <param name="pattern">Regex pattern</param>
		/// <param name="anchor">Levenshtein Distance anchor</param>
		/// <param name="threshold">Levenshtein Distance threshold</param>
		/// <returns></returns>
		private List<Web_src_info> Get_info_list_from_html(Web_src_info src_info, params string[] func_list)
		{
			List<Web_src_info> info_list = new List<Web_src_info>();
			string html = "";
			string host = get_host(src_info.Url);

			WebClient wc = new WebClient();
			try
			{
				html = wc.DownloadString(src_info.Url);

				JintEngine js_engine = create_js_engine();
				js_engine.SetParameter("settings", MainWindow.Main.Settings);
				js_engine.SetParameter("html", html);
				js_engine.SetParameter("src_info", src_info);
				js_engine.SetParameter("info_list", info_list);

				foreach (var func in func_list)
				{
					js_engine.Run(string.Format("{0}.{1}();", host, func));
				}

				var distinct_list = info_list.Distinct(new Web_src_info.Comparer()).Cast<List<Web_src_info>>();

				src_info.Children = distinct_list as List<Web_src_info>;
				src_info.Cookie = wc.ResponseHeaders["Set-Cookie"];
			}
			catch (Exception ex)
			{
				if (!(ex is ThreadAbortException))
					Log_error(ex, src_info.Url);
			}

			return info_list;
		}

		private JintEngine create_js_engine()
		{
			JintEngine js_engine = new JintEngine();
			js_engine.DisableSecurity();
			js_engine.SetFunction(
				"levenshtein_distance",
				new Func<string, string, int>((s, t) => { return ys.Common.LevenshteinDistance(s, t); })
			);
			js_engine.SetFunction(
				"matches",
				new Func<string, object, MatchCollection>((input, pattern) => { return Regex.Matches(input, pattern.ToString().Trim('/'), RegexOptions.IgnoreCase); })
			);
			js_engine.SetFunction("tostr", new Func<string, string>((s) => { return s; }));
			js_engine.SetFunction(
				"report",
				new Action<object>((info) => { Report(info.ToString()); })
			);
			js_engine.SetFunction(
				"web_src_info",
				new Func<string, double, string, Web_src_info, Web_src_info>((url, index, name, parent) => 
				{
					return new Web_src_info(url, (int)index, ys.Common.Format_for_number_sort(name.Trim()), parent);
				})
			);

			js_engine.Run(script);

			return js_engine;
		}

		private string get_host(string url)
		{
			return Regex.Match(url, @"(http://)?(.+?)?\.(?<host>.+?)/").Groups["host"].Value.Replace('.', '_');
		}
	}
}
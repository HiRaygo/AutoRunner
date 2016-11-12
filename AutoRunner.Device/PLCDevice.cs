/*
 * Created by SharpDevelop.
 * User: XiaoSanYa
 * Date: 2016/10/30
 * Time: 17:36
 * 
 * To change this template use Tools | Options | Coding | Edit Standard Headers.
 */
using System;
using System.Collections;
using System.Collections.Generic;
using System.Xml;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace AutoRunner.Device
{
	
	/// <summary>
	/// 异步通讯结构体
	/// </summary>
	public class StateObject
	{
	    // Client socket.
	    public Socket workSocket = null;
	    // Size of receive buffer.
	    public const int BufferSize = 256;
	    // Receive buffer.
	    public byte[] buffer = new byte[BufferSize];
	    // Received data string.
	    public StringBuilder sb = new StringBuilder();
	}
	
	/// <summary>
	/// 动作类型
	/// </summary>
	public enum ACTION_TYPE
	{
		RUN = 0,
		MOVE,
		CARRY,
		SETREG
	}
	
	/// <summary>
	/// 自动流水线上的一个物理位置，如上机位，测试位
	/// </summary>
	public class Point
	{
		//名称和描述
		public string Name;
		//正常与否控制位
		public bool TobeOK;	
		//容量		
		public int Capacity;
		//存量			
		public int Stock;
		
		public Point(string name, int capacity = 1, int stock = 0)
		{
			Name = name;
			Capacity = capacity;
			Stock = stock;
			
			TobeOK = true;
		}
		
		//是否可用
		public bool IsReady()
		{
			if(TobeOK)
			{
				if((Capacity - Stock)> 0)
					return true;
				else
					return false;
			}
			else
				return false;
		}
	}
	
	/// <summary>
	/// PLC的M、D软元寄存器
	/// </summary>
	public class MDReg
	{
		public string Name;
		public int Value;
		
		public MDReg(string name, int value)
		{
			Name = name;
			Value = value;
		}
	}
	
	/// <summary>
	/// 执行机构
	/// </summary>
	public class Actuator
	{
		public string Name;
		public bool TobeOK;
		public bool IsRunning;
		
		public Actuator(string name)
		{
			Name = name;
			TobeOK = true;
			IsRunning = false;
		}
		
		public bool IsReady()
		{
			if(TobeOK)
			{
				if(IsRunning)
					return false;
				else
					return true;
			}
			else
				return false;
		}
	}

	
	/// <summary>
	/// 从一个点到另一个点的动作，如上机
	/// </summary>
	public class Action
	{
		public string Name;
		public ACTION_TYPE Type;
		public string ActorName;
		public string DoActCode;
		public string RequireCode;	
		public string RunCode;
		public string DoneCode;
		public string Src;
		public string Dst;
		public long Timecost;		
		public long Starttime;
		public bool ToRun;
		public bool isRunning;
		
		public Action(string name)
		{
			Name = name;
			ToRun = true;
		}
		
		public Action(string name,string type, string actor, string doact, string require,string run,string done, string src, string dst, long time)
		{
			Name = name;
			if(type == "RUN")
				Type = ACTION_TYPE.RUN;
			else if(type == "MOVE")
				Type = ACTION_TYPE.MOVE;
			else if(type == "CARRY")
				Type = ACTION_TYPE.CARRY;
			else
				Type = ACTION_TYPE.SETREG;
			
			ActorName = actor;
			DoActCode = doact;
			RequireCode = require;
			RunCode = run;
			DoneCode = done;
			Src = src;
			Dst = dst;
			Timecost = time;
			ToRun = true;
		}

	}
	
	/// <summary>
	/// PLC Device
	/// </summary>
	public class PLCDevice
	{
		public string Name;
		public string Desc;
		public bool TobeOK;
		public bool ToRun;
				
		private Hashtable Points;
		private Hashtable Actuators;
		private Hashtable MDRegs;
		private Hashtable Actions;
		
		string IP;
		int Port;
		IPEndPoint ipe;
		const int MAXCLIENTS = 10;	
		private ManualResetEvent allDone;
		Thread commtrd;
		Thread actiontrd;
		bool isconfiged;
		bool isaddressed;
		
		public bool IsRunning;	//运行状态标志		
		public bool IsServering;
		
		public PLCDevice()
		{
			TobeOK = true;
			ToRun = false;
			isconfiged = false;
			isaddressed = false;
			
			Points = new Hashtable();
			Actuators = new Hashtable();
			MDRegs = new Hashtable();
			Actions = new Hashtable();
			
			IsServering = false;
			allDone = new ManualResetEvent(false);
			
		}
		
		/// <summary>
		/// 从文件获取配置，初始化各个集
		/// </summary>
		/// <param name="filepath"></param>
		/// <returns></returns>
		public bool ConfigFromFile(string filepath)
		{
			XmlDocument xmlDoc=new XmlDocument(); 
			xmlDoc.Load(filepath); 
			XmlNode pntNode = xmlDoc.SelectSingleNode("/PLC/Points");
			XmlNode atrNode = xmlDoc.SelectSingleNode("/PLC/Actuators");
			XmlNode mdrNode = xmlDoc.SelectSingleNode("/PLC/MDRegs");
			XmlNode actNode = xmlDoc.SelectSingleNode("/PLC/Actions");
			
			try{				
				foreach(XmlNode xn in pntNode.ChildNodes)
				{
					XmlElement xe = (XmlElement)xn;					
					string name = xe.GetAttribute("Name");
					int capacity = int.Parse(xe.GetAttribute("Capacity"));
					int stock = int.Parse(xe.GetAttribute("Stock"));
					Points.Add(name, new Point(name, capacity, stock));						
				}
				
				foreach(XmlNode xn in atrNode.ChildNodes)
				{
					XmlElement xe = (XmlElement)xn;					
					string name = xe.GetAttribute("Name");
					Actuators.Add(name, new Actuator(name));					
				}
				
				foreach(XmlNode xn in mdrNode.ChildNodes)
				{
					XmlElement xe = (XmlElement)xn;					
					string name = xe.GetAttribute("Name");
					int value = int.Parse(xe.GetAttribute("Value"));
					MDRegs.Add(name, value);					
				}
				
				foreach(XmlNode xn in actNode.ChildNodes)
				{
					XmlElement xe = (XmlElement)xn;					
					string name = xe.GetAttribute("Name");
					Action act = new Action(name);
					
					string type = xe.GetAttribute("Type");
					if(type == "CARRY")
						act.Type = ACTION_TYPE.CARRY;
					else if(type == "MOVE")
						act.Type = ACTION_TYPE.MOVE;
					else if(type == "RUN")
						act.Type = ACTION_TYPE.RUN;
					else 
						act.Type = ACTION_TYPE.SETREG;
					
					act.RequireCode = xe.GetAttribute("RequireCode");
					act.RunCode = xe.GetAttribute("RunCode");
					act.DoneCode = xe.GetAttribute("DoneCode");
					act.Src = xe.GetAttribute("SrcPoint");
					act.Dst = xe.GetAttribute("DstPoint");
					act.ActorName = xe.GetAttribute("ActorName");
					act.Timecost = long.Parse(xe.GetAttribute("TimeCost"));
					
					Points.Add(name, act);
				}
			}
			catch{
				return false;
			}
			isconfiged = true;
			return true;
		}
		
		
		/// <summary>
		/// 设置PLC的IP和Port
		/// </summary>
		/// <param name="ip"></param>
		/// <param name="port"></param>
		public bool SetAddress(string ip, int port)
		{
			try{
				IP = ip;
				Port = port;
				ipe = new IPEndPoint(IPAddress.Parse(IP), Port);
			}
			catch{
				return false;
			}
			isaddressed = true;
			return true;
		}
		

		/// <summary>
		/// 设置某动作的执行时间
		/// </summary>
		/// <param name="action">动作名称</param>
		/// <param name="value">执行时间</param>
		/// <returns></returns>
		public bool SetActionTime(string action, int value)
		{
			try{
				((Action)Actions[action]).Timecost = value;
			}
			catch{
				return false;
			}
			return true;
		}
		
		
		/// <summary>
		/// 控制某动作是否执行
		/// </summary>
		/// <param name="action">动作名称</param>
		/// <param name="run">执行控制位</param>
		/// <returns></returns>
		public bool SetActionRun(string action, bool run)
		{
			try{
				((Action)Actions[action]).ToRun = run;
			}
			catch{
				return false;
			}
			return true;
		}
		
		/// <summary>
		/// 设置软元的值
		/// </summary>
		/// <param name="reg">软元名称</param>
		/// <param name="value">值</param>
		/// <returns></returns>
		public bool SetReg(string reg, int value)
		{
			try{
				MDRegs[reg] = value;
			}
			catch{
				return false;
			}
			return true;
		}
		
		/// <summary>
		/// 查询软元的值
		/// </summary>
		/// <returns></returns>
		public bool QueryReg(string reg, out int value)
		{
			try{
		        value = (int)MDRegs[reg];
			}
			catch{
				value = 0;
				return false;
			}
			return true;
		}
		
		/// <summary>
		/// 设置点的参数
		/// </summary>
		/// <param name="point">名称</param>
		/// <param name="capacity">容量</param>
		/// <param name="stock">存量</param>
		/// <returns></returns>
		public bool SetPoint(string point, int capacity, int stock)
		{
			try{
				Point pt = (Point)Points[point];
				pt.Capacity = capacity;
				pt.Stock = stock;
			}
			catch{
				return false;
			}
			return true;
		}
		
		/// <summary>
		/// 查询点的状态
		/// </summary>
		/// <param name="point">名称</param>
		/// <param name="capacity">容量</param>
		/// <param name="stock">存量</param>
		/// <returns></returns>
		public bool QueryPoint(string point, out int capacity, out int stock)
		{
			try{
				Point pt = (Point)Points[point];
				capacity = pt.Capacity;
				stock = pt.Stock;
			}
			catch{				
				capacity = 1;
				stock = 0;
				return false;
			}
			return true;
		}
		
		/// <summary>
		/// 启动
		/// </summary>
		public bool StartDevice()
		{
			isconfiged =true;
			if((!isaddressed) || (!isconfiged))
				return false;
			if(IsRunning)
				return false;
			
			ToRun = true;
			
			//启动通讯线程
			commtrd = new Thread(new ThreadStart(StartServer));
			commtrd.IsBackground = true;
			commtrd.Start();
			
			//启动动作线程
			actiontrd = new Thread(new ThreadStart(ActionLoop));
			actiontrd.IsBackground = true;
			actiontrd.Start();
			
			IsRunning = true;
			return true;
		}
		
		/// <summary>
		/// 停止PLC Device
		/// </summary>
		public void StopDevice()
		{
			if(IsRunning)
			{
				ToRun = false;
				commtrd.Abort();
			}
		}
		
		/// <summary>
		/// 动作循环处理
		/// </summary>
		private void ActionLoop()
		{
			while(ToRun)
			{
				Thread.Sleep(20);
				long runtime = DateTime.Now.Ticks;
				foreach(DictionaryEntry de in Actions)
				{
					Action act = (Action)de.Value;
					int doact = (int)MDRegs[act.DoActCode];
					if(doact == 0)
					{
						ActionWait(act);
					}
					else
					{
						ActionRun(act, runtime);
					}
				}
			}
		}
		
		/// <summary>
		/// 动作等待执行
		/// </summary>
		/// <param name="act"></param>
		private void ActionWait(Action act)
		{
			if(act.isRunning) return;
			
			Actuator actor = (Actuator)Actuators[act.ActorName];
			if(!actor.IsReady())
			{
				MDRegs[act.RequireCode] = 0;
			}
			else
			{
				if(act.Type == ACTION_TYPE.CARRY)
				{
					Point sp = (Point)Points[act.Src];
					Point dp = (Point)Points[act.Dst];
					if((sp.Stock >0) && (dp.Capacity - dp.Stock >0))
					{
						MDRegs[act.RequireCode] = 1;
					}
					else
					{
						MDRegs[act.RequireCode] = 0;
						return;
					}
				}
				MDRegs[act.RequireCode] = 1;
			}
		}
		
		/// <summary>
		/// 动作执行
		/// </summary>
		/// <param name="act"></param>
		private void ActionRun(Action act, long runtime)
		{
			//第一次运行
			if(!act.isRunning)
			{
				act.isRunning = true;
				act.Starttime = runtime;
				MDRegs[act.RequireCode] = 0;
				MDRegs[act.RunCode] = 1;
				MDRegs[act.DoneCode] = 0;
				if(act.Type != ACTION_TYPE.SETREG)
				{
					((Actuator)Actuators[act.ActorName]).IsRunning = true;
				}
				return;
			}
			//运行时间到了
			if(runtime - act.Starttime >= act.Timecost *10000)
			{
				if(act.Type == ACTION_TYPE.SETREG)
				{
					MDRegs[act.Dst] = int.Parse(act.Src);
				}
				else if(act.Type == ACTION_TYPE.CARRY)
				{
					Point sp = (Point)Points[act.Src];
					Point dp = (Point)Points[act.Dst];
					sp.Stock -= 1;
					dp.Stock += 1;
				}
				((Actuator)Actuators[act.ActorName]).IsRunning = true;
				MDRegs[act.RunCode] = 0;
				MDRegs[act.DoneCode] = 1;
				MDRegs[act.DoActCode] = 0;	//完成后动作不再重复，清除标志
				act.isRunning = false;
			}			
		}	
		
		/// <summary>
		/// 启动PLC Server
		/// </summary>
		private void StartServer()
		{
			Socket listener = new Socket(SocketType.Stream,ProtocolType.Tcp);
			try{
				listener.Bind(ipe);
				listener.Listen(MAXCLIENTS);
				IsServering = true;
				
				while(ToRun)
				{
					allDone.Reset();
	                // Start an asynchronous socket to listen for connections.
	                listener.BeginAccept(new AsyncCallback(AcceptCallback), listener);
	                // Wait until a connection is made before continuing.
	                allDone.WaitOne();
				}
				listener.Shutdown(SocketShutdown.Both);
				listener.Close();
			}
			catch (Exception e){
            	Console.WriteLine(e.ToString());
			}
			IsServering = false;
		}

		
		private void AcceptCallback(IAsyncResult ar)
	    {
	        // Signal the main thread to continue.
	        allDone.Set();
	        // Get the socket that handles the client request.
	        Socket listener = (Socket)ar.AsyncState;
	        Socket handler = listener.EndAccept(ar);
	        // Create the state object.
	        StateObject state = new StateObject();
	        state.workSocket = handler;
	        handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,new AsyncCallback(ReceiveCallback), state);
	    }
		
		
	    private void ReceiveCallback(IAsyncResult ar)
	    {
	        String content = String.Empty;
	        // Retrieve the state object and the handler socket
	        StateObject state = (StateObject)ar.AsyncState;
	        Socket handler = state.workSocket;
	        // Read data from the client socket.
	        int bytesRead = handler.EndReceive(ar);
	        if (bytesRead > 0)
	        {
	            // There might be more data, so store the data received so far.
	            state.sb.Append(Encoding.ASCII.GetString(state.buffer, 0, bytesRead));
	            content = state.sb.ToString();
	            if (content.IndexOf("<EOF>") > -1)
	            {
	                //Console.WriteLine("Read {0} bytes from socket. \n Data : {1}", content.Length, content);
	                byte[] byteData = Encoding.ASCII.GetBytes(content);
	                handler.BeginSend(byteData, 0, byteData.Length, 0, new AsyncCallback(SendCallback), handler);
	            }
	            else{
	                // Not all data received. Get more.
	                handler.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
	                new AsyncCallback(ReceiveCallback), state);
	            }
	        }
	    }
	    
	    	    
	    private void SendCallback(IAsyncResult ar)
	    {
	        try{
	            // Retrieve the socket from the state object.
	            Socket handler = (Socket)ar.AsyncState;
	            int bytesSent = handler.EndSend(ar);
	            //handler.Shutdown(SocketShutdown.Both);
	            //handler.Close();
	        }
	        catch (Exception e){
	            Console.WriteLine(e.ToString());
	        }
	    }
	}
}

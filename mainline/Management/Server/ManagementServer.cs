﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using SuperSocket.Common;
using SuperSocket.Management.Shared;
using SuperSocket.SocketBase;
using SuperSocket.SocketBase.Config;
using SuperSocket.SocketBase.Protocol;
using SuperWebSocket;
using SuperWebSocket.Protocol;
using SuperWebSocket.SubProtocol;
using SuperSocket.Management.Server.Config;

namespace SuperSocket.Management.Server
{
    public class ManagementServer : WebSocketServer<ManagementSession>
    {
        private IServerContainer m_ServerContainer;

        private Dictionary<string, UserConfig> m_UsersDict;

        public ManagementServer()
            : base(new BasicSubProtocol<ManagementSession>("ServerManager"))
        {

        }

        public override bool Setup(IRootConfig rootConfig, IServerConfig config, ISocketServerFactory socketServerFactory, IRequestFilterFactory<IWebSocketFragment> protocol)
        {
            if (!base.Setup(rootConfig, config, socketServerFactory, protocol))
                return false;

            var users = config.GetChildConfig<UserConfigCollection>("users");

            if (users == null || users.Count <= 0)
            {
                Logger.Error("No user defined");
                return false;
            }

            m_UsersDict = new Dictionary<string, UserConfig>(StringComparer.OrdinalIgnoreCase);

            foreach (var u in users)
            {
                m_UsersDict.Add(u.Name, u);
            }

            return true;
        }

        protected override void OnStartup()
        {
            m_ServerContainer = GetService<IServerContainer>();
            m_ServerContainer.Loaded += new EventHandler(m_ServerContainer_Loaded);
            m_ServerContainer.PerformanceDataCollected += new EventHandler<PermformanceDataEventArgs>(m_ServerContainer_PerformanceDataCollected);

            base.OnStartup();
        }

        protected override void OnStopped()
        {
            if (m_ServerContainer != null)
            {
                m_ServerContainer.Loaded -= new EventHandler(m_ServerContainer_Loaded);
                m_ServerContainer.PerformanceDataCollected -= new EventHandler<PermformanceDataEventArgs>(m_ServerContainer_PerformanceDataCollected);
                m_ServerContainer = null;
            }

            base.OnStopped();
        }

        private ServerState m_ServerState;

        internal ServerInfo CurrentServerInfo { get; private set; }

        void m_ServerContainer_PerformanceDataCollected(object sender, PermformanceDataEventArgs e)
        {
            m_ServerState.GlobalPerformance = e.GlobalData;

            var performanceDict = new Dictionary<string, PerformanceData>(e.InstancesData.Length, StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < e.InstancesData.Length; i++)
            {
                var p = e.InstancesData[i];
                performanceDict.Add(p.ServerName, p.Data);
            }

            for (var i = 0; i < m_ServerState.InstanceStates.Length; i++)
            {
                var s = m_ServerState.InstanceStates[i];

                PerformanceData p;

                if (performanceDict.TryGetValue(s.Instance.Name, out p))
                    s.Performance = p;
            }

            CurrentServerInfo = m_ServerState.ToServerInfo();

            var content = CommandName.UPDATE + " " + JsonConvert.SerializeObject(CurrentServerInfo);

            foreach (var s in GetSessions(s => s.LoggedIn))
            {
                s.SendResponseAsync(content);
            }
        }

        internal ServerInfo GetUpdatedCurrentServerInfo()
        {
            CurrentServerInfo = m_ServerState.ToServerInfo();
            return CurrentServerInfo;
        }

        void m_ServerContainer_Loaded(object sender, EventArgs e)
        {
            m_ServerState = new ServerState
            {
                InstanceStates = m_ServerContainer.GetAllServers().Where(s => s != this).Select(s => new InstanceState
                {
                    Instance = s
                }).ToArray()
            };
        }

        internal IAppServer GetServerByName(string name)
        {
            return m_ServerContainer.GetServerByName(name);
        }

        internal UserConfig GetUserByName(string name)
        {
            UserConfig user;
            m_UsersDict.TryGetValue(name, out user);
            return user;
        }
    }
}

using System;
using System.Text;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Threading.Tasks;
using log4net;
using SZ2.WebSocketGaugeServer.WebSocketDataLogger.FUELTRIPLogger.Service.FUELTripCalculator;
using SZ2.WebSocketGaugeServer.WebSocketDataLogger.FUELTRIPLogger.SessionItems;
using SZ2.WebSocketGaugeServer.WebSocketDataLogger.FUELTRIPLogger.Settings;
using SZ2.WebSocketGaugeServer.WebSocketDataLogger.FUELTRIPLogger.JSONFormat;
using Microsoft.Extensions.Hosting;
using System.IO;
using Newtonsoft.Json;
using Microsoft.Extensions.Configuration;

namespace SZ2.WebSocketGaugeServer.WebSocketDataLogger.FUELTRIPLogger.Service
{
    public class FUELTRIPService : IDisposable
    {
        private static readonly ILog logger = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        private readonly WebSocketClients wsClients;
        private readonly FuelTripCalculator fuelTripCalc;
        private readonly Dictionary<Guid, (WebSocket WebSocket, FUELTRIPWebSocketSessionParam SessionParam)> WebSocketDictionary = new Dictionary<Guid, (WebSocket WebSocket, FUELTRIPWebSocketSessionParam SessionParam)>();

        public void AddWebSocket(Guid sessionGuid, WebSocket websocket)
        {
            this.WebSocketDictionary.Add(sessionGuid, (websocket, new FUELTRIPWebSocketSessionParam()));
        }

        public void RemoveWebSocket(Guid sessionGuid)
        {
            this.WebSocketDictionary.Remove(sessionGuid);
        }

        public FUELTRIPWebSocketSessionParam GetSessionParam(Guid guid)
        {
            return this.WebSocketDictionary[guid].SessionParam;
        }

        public FuelTripCalculator FUELTripCalculator { get { return this.fuelTripCalc; } }

        public FUELTRIPService(IConfiguration configuration, IHostApplicationLifetime lifetime)
        {
            var appSettings = JsonConvert.DeserializeObject<FUELTRIPLoggerSettings>(File.ReadAllText("./fueltriplogger_settings.jsonc"));
            this.fuelTripCalc = new FuelTripCalculator(appSettings.Calculation.CalculationOption, appSettings.Calculation.FuelCalculationMethod);

            //Websocket clients setup
            this.wsClients = new WebSocketClients(appSettings);

            var cancellationToken = lifetime.ApplicationStopping;

            this.wsClients.VALMessageParsed += async (sender, args) =>
            {
                try
                {
                    fuelTripCalc.update(wsClients.EngineRev, wsClients.VehicleSpeed, wsClients.InjPulseWidth, wsClients.MassAirFlow, wsClients.AFRatio, wsClients.FuelRate);
                    foreach (var session in this.WebSocketDictionary)
                    {
                        var guid = session.Key;
                        var websocket = session.Value.WebSocket;
                        var sessionparam = session.Value.SessionParam;

                        // Construct momentun fuel trip data message.
                        string msg = this.constructMomentumFUELTRIPDataMessage(fuelTripCalc).Serialize();
                        // Send message,
                        byte[] buf = Encoding.UTF8.GetBytes(msg);
                        await websocket.SendAsync(new ArraySegment<byte>(buf), WebSocketMessageType.Text, true, cancellationToken);
                    }

                }
                catch (TimeoutException ex)
                {
                    logger.Warn("TimeOutException is occured on FUELTRIP calcilation. FUELTRIP calutaion is skipped on this tick. ");
                    logger.Warn(ex.Message);
                }
                catch (WebSocketException ex)
                {
                    logger.Warn(ex.GetType().FullName + " : " + ex.Message + " : Error code : " + ex.ErrorCode.ToString());
                    logger.Warn(ex.StackTrace);
                }
            };

            this.fuelTripCalc.SectFUELTRIPUpdated += async (sender, e) =>
            {
                try
                {
                    foreach (var session in this.WebSocketDictionary)
                    {
                        var guid = session.Key;
                        var websocket = session.Value.WebSocket;
                        var sessionparam = session.Value.SessionParam;

                        // Construct section fuel trip data message.
                        string msg = this.constructSectionFUELTRIPDataMessage(fuelTripCalc).Serialize();
                        // Send message,
                        byte[] buf = Encoding.UTF8.GetBytes(msg);
                        await websocket.SendAsync(new ArraySegment<byte>(buf), WebSocketMessageType.Text, true, cancellationToken);
                    }
                }
                catch (WebSocketException ex)
                {
                    logger.Warn(ex.GetType().FullName + " : " + ex.Message + " : Error code : " + ex.ErrorCode.ToString());
                    logger.Warn(ex.StackTrace);
                }
            };

            this.fuelTripCalc.loadTripFuel();
            this.wsClients.start();

            logger.Info("Websocket server is started");
        }

        public void Dispose()
        {
            fuelTripCalc.saveTripFuel();
            logger.Info("FUELTRIP data is saved.");
            var stopTask = Task.Run(() => this.wsClients.stop());
            Task.WhenAny(stopTask, Task.Delay(10000));
            logger.Info("Websocket server is stopped");
        }

        private FUELTRIPJSONFormat constructMomentumFUELTRIPDataMessage(FuelTripCalculator fuelTripCalculator)
        {
            var fueltrip_json = new FUELTRIPJSONFormat();
            fueltrip_json.moment_gasmilage = fuelTripCalculator.MomentaryTripPerFuel;
            fueltrip_json.total_gas = fuelTripCalculator.TotalFuelConsumption;
            fueltrip_json.total_trip = fuelTripCalculator.TotalTrip;
            fueltrip_json.total_gasmilage = fuelTripCalculator.TotalTripPerFuel;

            return fueltrip_json;
        }

        private SectFUELTRIPJSONFormat constructSectionFUELTRIPDataMessage(FuelTripCalculator fuelTripCalculator)
        {
            var sectfueltrip_json = new SectFUELTRIPJSONFormat();
            sectfueltrip_json.sect_gas = fuelTripCalculator.SectFuelArray;
            sectfueltrip_json.sect_trip = fuelTripCalculator.SectTripArray;
            sectfueltrip_json.sect_gasmilage = fuelTripCalculator.SectTripPerFuelArray;
            sectfueltrip_json.sect_span = fuelTripCalculator.SectSpan;

            return sectfueltrip_json;
        }
    }
}
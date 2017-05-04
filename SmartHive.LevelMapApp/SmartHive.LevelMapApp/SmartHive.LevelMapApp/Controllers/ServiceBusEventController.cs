﻿using System;
using SmartHive.Models.Config;
using SmartHive.Models.Events;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SmartHive.LevelMapApp.Controllers
{
    public  class ServiceBusEventController
    {
        private IEventTransport transport;
        private ILevelMapController mapController;
        private ILevelConfig levelConfig;

        event EventHandler<IRoomSensor> OnRoomSensorChanged;
        event EventHandler<Appointment> OnRoomScheduleStatusChanged;

        public ServiceBusEventController(IEventTransport transport, ISettingsProvider settingsProvider)
        {
            //TODO : Add Factory method to choose map provider
            this.mapController = new WireGeoRoomController(settingsProvider);
            this.OnRoomSensorChanged += this.mapController.OnRoomSensorChanged;

            string levelId = settingsProvider.GetPropertyValue(SettingsConst.DefaultLevel_PropertyName);
            this.levelConfig = settingsProvider.GetLevelConfig(levelId);

            this.transport = transport;            
            
            // Check if settings loaded or wait until Configuration will be ready for that
            if (this.levelConfig.isLoaded)
                InitTransport();
            else
                this.levelConfig.OnSettingsLoaded += LevelConfig_OnSettingsLoaded;
        }       

        private void InitTransport()
        {
            this.transport.OnServiceBusConnected += Transport_OnServiceBusConnected;            
            this.transport.Connect(this.levelConfig);
        }

        private void LevelConfig_OnSettingsLoaded(object sender, bool e)
        {
            InitTransport();
        }

        private void Transport_OnServiceBusConnected(object sender, string e)
        {
            this.transport.OnScheduleUpdate += Transport_OnScheduleUpdate;
            this.transport.OnNotification += Transport_OnNotification;
        }

        private void Transport_OnNotification(object sender, OnNotificationEventArgs e)
        {
            IRoomConfig roomConfig = this.levelConfig.GetRoomConfigForSensorDeviceId(e.DeviceId);
            if (roomConfig != null)
            {
                var sensor = roomConfig.RoomSensors.FirstOrDefault<IRoomSensor>(s => s.DeviceId.Equals(e.DeviceId) && s.Telemetry.Equals(e.ValueLabel));

                bool IsChanged = false;
                                                
                if (sensor != null)
                {
                    // Check if value was changed
                    IsChanged = sensor.LastMeasurement == null|| (!string.IsNullOrEmpty(sensor.LastMeasurement.Value) && !sensor.LastMeasurement.Value.Equals(e.Value)); 
                    sensor.LastMeasurement = e;
                    if (IsChanged && this.OnRoomSensorChanged != null)
                    {
                        UpdateRoomStatus(roomConfig, sensor);
                        this.OnRoomSensorChanged.Invoke(roomConfig, sensor);
                    }
                }
            }
        }


        /// <summary>
        /// Calculate IRoomConfig.RoomStatus property based on last events
        /// </summary>
        /// <param name="roomConfig"></param>
        /// <param name="sensor"></param>
        private void UpdateRoomStatus(IRoomConfig roomConfig, IRoomSensor sensor)
        {
            if (roomConfig == null)
                return; // Nothing to do;

            if (sensor == null || !NotificationEventSchema.PirSensorValueLabel.Equals(sensor.Telemetry)) // Extract sensor from Room Config
            {
                sensor = roomConfig.RoomSensors.FirstOrDefault<IRoomSensor>(s => NotificationEventSchema.PirSensorValueLabel.Equals(sensor.Telemetry));
            }

            if (roomConfig.CurrentAppointment != null && DateTime.Now >= DateTime.Parse(roomConfig.CurrentAppointment.EndTime))
            { // Check if room appointment expired
                roomConfig.CurrentAppointment = null;
            }

            double PiR = 0;

            if (sensor != null && NotificationEventSchema.PirSensorValueLabel.Equals(sensor.Telemetry))// PiR sensor changed
            {
                Double.TryParse(sensor.LastMeasurement.Value, out PiR);            
            }

            if (PiR > 0.0) //Presense detected
            {
                if (roomConfig.CurrentAppointment != null)
                    roomConfig.RoomStatus = RoomStatus.RoomScheduledAndOccupied;
                else
                    roomConfig.RoomStatus = RoomStatus.RoomOccupied;
            }
            else // No presence in the room
            {
                if (roomConfig.CurrentAppointment != null)
                    roomConfig.RoomStatus = RoomStatus.RoomScheduled;
                else
                    roomConfig.RoomStatus = RoomStatus.RoomFree;
            }
        }


        private void Transport_OnScheduleUpdate(object sender, OnScheduleUpdateEventArgs e)
        {
            IRoomConfig roomConfig = this.levelConfig.GetRoomConfig(e.RoomId);
            if (roomConfig == null)
                return;

            Appointment currentAppointment = null;
            if (e.Schedule != null &&  e.Schedule.Length > 0)
            {
                currentAppointment = e.Schedule.SingleOrDefault<Appointment>(a => DateTime.Parse(a.StartTime) >= DateTime.Now && DateTime.Parse(a.EndTime) <= DateTime.Now);               
            }

            bool IsChanged = roomConfig.CurrentAppointment == currentAppointment;

            if (currentAppointment != null)
                roomConfig.CurrentAppointment = currentAppointment;
            else
                roomConfig.CurrentAppointment = null;

            if (IsChanged && this.OnRoomScheduleStatusChanged != null)
            {
                UpdateRoomStatus(roomConfig, null);
                this.OnRoomScheduleStatusChanged(roomConfig, currentAppointment);
            }
        }
      
    }
}

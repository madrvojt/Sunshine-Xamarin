﻿using System;

using Android.OS;
using Android.Views;
using Android.Widget;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Sunshine.Data;
using Android.Support.V4.Content;
using Android.Support.V4.App;
using Android.Database;
using Android.Content;
using Sunshine.Service;
using Android.App;

namespace Sunshine
{
    /// <summary>
    ///  A placeholder fragment containing a simple view.
    /// </summary>
    public class ForecastFragment : Android.Support.V4.App.Fragment, Android.Support.V4.App.LoaderManager.ILoaderCallbacks
    {

        ForecastAdapter _forecastAdapter;
        const string SourceContext = "MyNamespace.MyClass";
        readonly ILogger _log;

        ListView _listView;
        int _position = AdapterView.InvalidPosition;
        const string SelectedKey = "selected_position";
        bool _useTodayLayout;

        const int ForecastLoader = 0;


        /// <summary>
        /// A callback interface that all activities containing this fragment must
        /// implement. This mechanism allows activities to be notified of item
        /// selections
        /// </summary>
        public interface ICallback
        {
            // DetailFragmentCallback for when an item has been selected.
            void OnItemSelected(Android.Net.Uri dateUri);
        }

        static string[] forecastColumns =
            {
                // In this case the id needs to be fully qualified with a table name, since
                // the content provider joins the location & weather tables in the background
                // (both have an _id column)
                // On the one hand, that's annoying.  On the other, you can search the weather table
                // using the location set by the user, which is only in the Location table.
                // So the convenience is worth it.
                WeatherContract.Weather.TableName + "." + WeatherContract.Weather.Id,
                WeatherContract.Weather.ColumnDate,
                WeatherContract.Weather.ColumnShortDescription,
                WeatherContract.Weather.ColumnMaximumTemp,
                WeatherContract.Weather.ColumnMinimumTemp,
                WeatherContract.Location.ColumnLocationSetting,
                WeatherContract.Weather.ColumnWeatherId,
                WeatherContract.Location.ColumnCoordinationLat,
                WeatherContract.Location.ColumnCoordinationLong
            };


        // These indices are tied to FORECAST_COLUMNS.  If FORECAST_COLUMNS changes, these
        // must change. - This is mapping index for projection (in this case forecastcolumns)
        public const int ColWeatherId = 0;
        public const int ColWeatherDate = 1;
        public const int ColWeatherDesc = 2;
        public const int ColWeatherMaxTemp = 3;
        public const int ColWeatherMinTemp = 4;
        public const int ColLocationSettings = 5;
        public const int ColWeatherConditionId = 6;
        public const int ColCoordLat = 7;
        public const int ColCoordLon = 8;

       

        public ForecastFragment()
        {
            var levelSwitch = new LoggingLevelSwitch();
            levelSwitch.MinimumLevel = LogEventLevel.Verbose;
            _log = new LoggerConfiguration().MinimumLevel.ControlledBy(levelSwitch).WriteTo.AndroidLog().CreateLogger();
        }

        public override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            // set this for handling menu events like onCreateOptionsMenu
            HasOptionsMenu = true;
        }

        public Android.Support.V4.Content.Loader OnCreateLoader(int id, Bundle args)
        {

            // This is called when a new Loader needs to be created.  This
            // fragment only uses one loader, so we don't care about checking the id.
            // To only show current and future dates, filter the query to return weather only for
            // dates after or including today.

            // Sort order:  Ascending, by date.

            const string sortOrder = WeatherContract.Weather.ColumnDate + " ASC";
            string locationSetting = Utility.GetPreferredLocation(Activity);

            var weatherForLocationUri = WeatherContract.Weather.BuildWeatherLocationWithStartDate(
                                            locationSetting, DateTime.UtcNow.Ticks);

            return new Android.Support.V4.Content.CursorLoader(Activity, weatherForLocationUri, forecastColumns, null, null, sortOrder);


        }

        public void SetUseTodayLayout(bool useTodayLayout)
        {
            _useTodayLayout = useTodayLayout;
            if (_forecastAdapter != null)
            {
                _forecastAdapter.UseTodayLayout = _useTodayLayout;
            }
        }


        public void OnLoadFinished(Android.Support.V4.Content.Loader loader, Java.Lang.Object data)
        {
            _forecastAdapter.SwapCursor((ICursor)data);

            if (_position != AdapterView.InvalidPosition)
            {
                // If we don't need to restart the loader, and there's a desired position to restore
                // to, do so now.
                _listView.SmoothScrollToPosition(_position);
            }  
        }

        public void OnLoaderReset(Android.Support.V4.Content.Loader loader)
        {
            _forecastAdapter.SwapCursor(null);

        }


        public override void OnActivityCreated(Bundle savedInstanceState)
        {
            LoaderManager.InitLoader(ForecastLoader, null, this);

            base.OnActivityCreated(savedInstanceState);
        }


        public override void OnCreateOptionsMenu(IMenu menu, MenuInflater inflater)
        {
            inflater.Inflate(Resource.Menu.forecastfragment, menu);
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {

            int id = item.ItemId;

            switch (id)
            {
                case Resource.Id.action_refresh:

                    UpdateWeather();                            
                    return true;

                default:
                    return base.OnOptionsItemSelected(item);
            }
        }


        public void OnLocationChanged()
        {
            UpdateWeather();
            LoaderManager.RestartLoader(ForecastLoader, null, this);
        }

        void UpdateWeather()
        {
            // Android times are quoted as milliseconds since start of 1970
            var dtBasis = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            var alarmIntent = new Intent(Activity, typeof(AlarmReceiver));
            alarmIntent.PutExtra(SunshineService.LocationQueryExtra, Utility.GetPreferredLocation(Activity));

            //Wrap in a pending intent which only fires once.
            var pi = PendingIntent.GetBroadcast(Activity, 0, alarmIntent, PendingIntentFlags.OneShot);
            var am = (AlarmManager)Activity.GetSystemService(Context.AlarmService);

            //Set the AlarmManager to wake up the system.
            am.Set(AlarmType.RtcWakeup, ((long)DateTime.UtcNow.Subtract(dtBasis).TotalMilliseconds) + 5000, pi);
        }


        public override View OnCreateView(LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
        {


            // The CursorAdapter will take data from our cursor and populate the ListView
            // However, we cannot use FLAG_AUTO_REQUERY since it is deprecated, so we will end
            // up with an empty list the first time we run.
            _forecastAdapter = new ForecastAdapter(Activity, null, 0);

            var rootView = inflater.Inflate(Resource.Layout.fragment_main, container, false);
            _listView = rootView.FindViewById<ListView>(Resource.Id.listview_forecast);

            // Reference to ListView
            _listView.Adapter = _forecastAdapter;



            _listView.ItemClick += (sender, e) =>
            {                   
                var cursor = (ICursor)((AdapterView)sender).GetItemAtPosition(e.Position);
                if (cursor != null)
                {
                    var locationSetting = Utility.GetPreferredLocation(Activity);
                    
                    ((ICallback)Activity).OnItemSelected(WeatherContract.Weather.BuildWeatherLocationWithDate(
                            locationSetting, cursor.GetLong(ColWeatherDate)));
                }
                _position = e.Position;
            };

            if (savedInstanceState != null && savedInstanceState.ContainsKey(SelectedKey))
            {
                // The listview probably hasn't even been populated yet.  Actually perform the
                // swapout in onLoadFinished.
                _position = savedInstanceState.GetInt(SelectedKey);
            }
            _forecastAdapter.UseTodayLayout = _useTodayLayout;

            return rootView;
        }

        public override void OnSaveInstanceState(Bundle outState)
        {
            if (_position != AdapterView.InvalidPosition)
            {
                outState.PutInt(SelectedKey, _position);
            }

            base.OnSaveInstanceState(outState);
        }

    }
}


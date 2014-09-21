﻿using Microsoft.Win32;
using SyncList;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace RemoteWindowsAdministrator {

	public class Win32Product: IContainsString {
		public string Name { get; set; }
		public string Publisher { get; set; }
		public string Version { get; set; }
		public DateTime? InstallDate { get; set; }
		public float? Size { get; set; }
		public bool CanRemove { get; set; }
		public bool SystemComponent { get; set; }
		public string Guid { get; set; }
		public string HelpLink { get; set; }
		public string Comment { get; set; }
		public string UrlInfoAbout { get; set; }

		public bool ContainsString( string value ) {
			return (new ValueIsIn( value )).Test( Name ).Test( Publisher ).Test( Version ).Test( InstallDate ).Test( Size ).Test( Guid ).Test( IsHidden( ) ).Test( HelpLink ).Test( UrlInfoAbout ).IsContained;
		}

		public bool Valid( ) {
			return !string.IsNullOrEmpty( Name ) && !string.IsNullOrEmpty( Guid );
		}

		private static bool HasGuid( IEnumerable<Win32Product> values, string guid ) {
			return values.Any( currentValue => guid.Equals( currentValue.Guid, StringComparison.OrdinalIgnoreCase ) );
		}

		private static string GetString( RegistryKey rk, string value ) {
			var obj = rk.GetValue( value );
			if( null == obj ) {
				return string.Empty;
			}
			var result = obj as string;
			if( result == null ) {
				return string.Empty;
			}
			result = result.Trim( );
			return result;
		}

		private static DateTime? GetDateTime( RegistryKey rk, string value ) {
			DateTime? result = null;
			var strDteTime = GetString( rk, value );
			if( string.IsNullOrEmpty( strDteTime ) ) {
				return null;
			}
			string[] dteFormats = { @"yyyy-MM-dd", @"yyyyMMdd", @"MM-dd-yyyy" };
			DateTime tmp;
			if( DateTime.TryParseExact( strDteTime, dteFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out tmp ) ) {
				result = tmp;
			}
			return result;
		}

		private static Int32? GetDword( RegistryKey rk, string value, Int32? defaultValue = null ) {
			var result = rk.GetValue( value ) as Int32?;
			if( null == result && null != defaultValue ) {
				result = defaultValue;
			}
			return result;
		}

		public bool ShouldHide { get { return IsHidden( ); } }

		private bool IsHidden( bool shown = false ) {
			return !shown && SystemComponent;
		}

		public static void FromComputerName( string computerName, ref SyncList<Win32Product> result, bool showHidden = false ) {
			try {
				string[] regPaths = { @"SOFTWARE\Wow6432node\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall" };
				foreach( var currentPath in regPaths ) {
					using( var regKey = RegistryKey.OpenRemoteBaseKey( RegistryHive.LocalMachine, computerName ).OpenSubKey( currentPath, false ) ) {
						if( null == regKey ) {
							continue;
						}
						SyncList<Win32Product> list = result;
						foreach( var curGuid in regKey.GetSubKeyNames( ).Where( currentGuid => currentGuid.StartsWith( @"{" ) && !HasGuid( list, currentGuid ) ) ) {
							using( var curReg = regKey.OpenSubKey( curGuid, false ) ) {
								if( null == curReg || !string.IsNullOrEmpty( GetString( curReg, @"ParentKeyName" ) ) ) {
									continue;
								}
								var currentProduct = new Win32Product {
									Guid = curGuid,
									Name = GetString( curReg, @"DisplayName" ),
									Publisher = GetString( curReg, @"Publisher" ),
									Version = GetString( curReg, @"DisplayVersion" ),
									InstallDate = GetDateTime( curReg, @"InstallDate" ),
									CanRemove = 0 == GetDword( curReg, @"NoRemove", 0 ),
									SystemComponent = 1 == GetDword( curReg, @"SystemComponent", 0 )
								};
								{
									var estSize = GetDword( curReg, @"EstimatedSize" );
									if( null != estSize ) {
										currentProduct.Size = (float)Math.Round( (float)estSize / 1024.0, 2, MidpointRounding.AwayFromZero );
									}
								}

								currentProduct.HelpLink = GetString( curReg, @"HelpLink" );
								currentProduct.Comment = GetString( curReg, @"Comment" );
								currentProduct.UrlInfoAbout = GetString( curReg, @"UrlInfoAbout" );
								if( currentProduct.Valid( ) && !currentProduct.IsHidden( showHidden ) ) {
									list.Add( currentProduct );
								}
							}
						}
					}
				}
			} catch( System.IO.IOException ) {
				MessageBox.Show( @"Error connecting to computer or another IO Error", @"Error", MessageBoxButtons.OK );
			} catch( UnauthorizedAccessException ) {
				MessageBox.Show( @"You do not have permission to open this computer", @"Error", MessageBoxButtons.OK );
			} catch( System.Security.SecurityException ) {
				MessageBox.Show( @"Security error while connecting", @"Error", MessageBoxButtons.OK );
			}
		}

		public static void UninstallGuidOnComputerName( string computerName, string guid ) {			
			WmiHelpers.ForEach( computerName, string.Format( @"SELECT * FROM Win32_Product WHERE IdentifyingNumber='{0}'", guid ), obj => {
				Debug.WriteLine( string.Format( @"Uninstalling '{0}' from {1}", obj.Properties["Name"].Value, computerName ) );
				var outParams = obj.InvokeMethod( @"Uninstall", null, null );
				Debug.Assert( outParams != null, @"Return value from uninstall was null.  This is not allowed" );
				var retVal = int.Parse( outParams[@"returnValue"].ToString( ) );
				if( 0 != retVal ) {
					MessageBox.Show( string.Format( @"Error uninstalling '{0}' from {1}. Returned a value of {2}", obj.Properties["Name"].Value, computerName, retVal ), @"Error", MessageBoxButtons.OK );
				}
			} );
		}
	}
}

package com.example.pocketfence

import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.net.wifi.WifiManager
import android.net.wifi.WifiManager.LocalOnlyHotspotReservation
import android.os.Build
import android.os.Handler
import android.os.Looper
import android.util.Log
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat
import io.flutter.embedding.android.FlutterActivity
import io.flutter.plugin.common.MethodChannel

/**
 * MainActivity provides a small bridge between Flutter and Android's
 * LocalOnlyHotspot API. It exposes a `pocketfence.hotspot` method channel
 * with `startHotspot` and `stopHotspot` methods.
 */
class MainActivity : FlutterActivity() {
	private val CHANNEL = "pocketfence.hotspot"
	private var hotspotReservation: LocalOnlyHotspotReservation? = null
	private val REQ_NEARBY = 1001

	// Pending hotspot request parameters while awaiting permission
	private var pendingName: String? = null
	private var pendingBlock: Boolean = false
	private var pendingDnsServers: List<String>? = null
	private var pendingResult: MethodChannel.Result? = null

	override fun configureFlutterEngine(flutterEngine: io.flutter.embedding.engine.FlutterEngine) {
		super.configureFlutterEngine(flutterEngine)
		MethodChannel(flutterEngine.dartExecutor.binaryMessenger, CHANNEL).setMethodCallHandler { call, result ->
			when (call.method) {
				"startHotspot" -> {
					val args = call.arguments as? Map<String, Any>
					val name = args?.get("ssid") as? String ?: "PocketFence"
					val block = args?.get("blockOthers") as? Boolean ?: false
					val dnsAny = args?.get("dnsServers") as? List<*>
					val dnsServers = dnsAny?.filterIsInstance<String>()
					startLocalOnlyHotspot(name, block, dnsServers, result)
				}
				"stopHotspot" -> stopLocalOnlyHotspot(result)
				else -> result.notImplemented()
			}
		}

		// Device discovery channel: list devices on local network / ARP table
		MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "pocketfence.devices").setMethodCallHandler { call, result ->
			when (call.method) {
				"listDevices" -> {
					try {
						val list = ArrayList<Map<String, String>>()
						// On emulators, return mocked devices for testing
						if (isProbablyEmulator()) {
							val m1: MutableMap<String, String> = HashMap()
							m1["ip"] = "10.0.2.2"
							m1["mac"] = "02:00:00:00:00:02"
							m1["name"] = "Android Emulator"
							m1["platform"] = "android"
							list.add(m1)
							result.success(list)
							return@setMethodCallHandler
						}
						try {
							val arp = java.io.File("/proc/net/arp")
							if (arp.exists()) {
								val lines = arp.readLines()
								for (i in 1 until lines.size) {
									val parts = lines[i].split(Regex("\\s+"))
									if (parts.size >= 4) {
										val ip = parts[0]
										val mac = parts[3]
										val entry: MutableMap<String, String> = HashMap()
										entry["ip"] = ip
										entry["mac"] = mac
										entry["name"] = ip
										entry["platform"] = "unknown"
										list.add(entry)
									}
								}
							}
						} catch (e: Exception) {
							// ignore and return what we have
						}
						result.success(list)
					} catch (e: Exception) {
						result.error("ERR", "Failed to list devices: ${e.message}", null)
					}
				}
				else -> result.notImplemented()
			}
		}
	}

	/**
	 * Entry point called from Flutter. Validates required permissions and
	 * on Android 13+ requests nearby/bluetooth permissions before proceeding.
	 */
	private fun startLocalOnlyHotspot(requestedName: String, blockOthers: Boolean, dnsServers: List<String>?, result: MethodChannel.Result) {
		// Ensure ACCESS_FINE_LOCATION is granted
		if (ContextCompat.checkSelfPermission(this, Manifest.permission.ACCESS_FINE_LOCATION) != PackageManager.PERMISSION_GRANTED) {
			result.error("MISSING_PERMISSION", "ACCESS_FINE_LOCATION is required. Request this permission before calling startHotspot.", null)
			return
		}

		// If running on an emulator, skip the NEARBY_DEVICES runtime check (emulators often cannot grant it)
		if (isProbablyEmulator()) {
			Log.w("PocketFence", "Emulator detected — returning mocked hotspot result for testing")
			val mock: MutableMap<String, Any> = HashMap()
			mock["ssid"] = requestedName
			mock["password"] = "emulator-test"
			if (dnsServers != null) mock["dnsServers"] = dnsServers
			Handler(Looper.getMainLooper()).post { result.success(mock) }
			return
		}

		// On Android 13+ we must also have nearby/bluetooth permissions; request them if missing
		if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
			val nearbyGranted = ContextCompat.checkSelfPermission(this, "android.permission.NEARBY_DEVICES") == PackageManager.PERMISSION_GRANTED
			val scanGranted = ContextCompat.checkSelfPermission(this, "android.permission.BLUETOOTH_SCAN") == PackageManager.PERMISSION_GRANTED
			val connectGranted = ContextCompat.checkSelfPermission(this, "android.permission.BLUETOOTH_CONNECT") == PackageManager.PERMISSION_GRANTED
			if (!nearbyGranted || !scanGranted || !connectGranted) {
				// store pending and request
				pendingName = requestedName
				pendingBlock = blockOthers
				pendingDnsServers = dnsServers
				pendingResult = result
				val toRequest = mutableListOf<String>()
				if (!nearbyGranted) toRequest.add("android.permission.NEARBY_DEVICES")
				if (!scanGranted) toRequest.add("android.permission.BLUETOOTH_SCAN")
				if (!connectGranted) toRequest.add("android.permission.BLUETOOTH_CONNECT")
				// Also include fine location to be safe
				if (ContextCompat.checkSelfPermission(this, Manifest.permission.ACCESS_FINE_LOCATION) != PackageManager.PERMISSION_GRANTED) {
					toRequest.add(Manifest.permission.ACCESS_FINE_LOCATION)
				}
				ActivityCompat.requestPermissions(this, toRequest.toTypedArray(), REQ_NEARBY)
				return
			}
		}

		startLocalOnlyHotspotInternal(requestedName, blockOthers, dnsServers, result)
	}

	/**
	 * Internal hotspot starter. Calls WifiManager.startLocalOnlyHotspot and
	 * returns the SSID/password via the provided MethodChannel.Result.
	 */
	private fun startLocalOnlyHotspotInternal(requestedName: String, blockOthers: Boolean, dnsServers: List<String>?, result: MethodChannel.Result) {
		val wifiManager = applicationContext.getSystemService(Context.WIFI_SERVICE) as WifiManager
		try {
			wifiManager.startLocalOnlyHotspot(object : WifiManager.LocalOnlyHotspotCallback() {
				override fun onStarted(reservation: LocalOnlyHotspotReservation) {
					super.onStarted(reservation)
					hotspotReservation = reservation
					val config = reservation.wifiConfiguration
					val ssid = config?.SSID ?: requestedName
					val pass = config?.preSharedKey ?: ""
					val map: MutableMap<String, Any> = HashMap()
					map["ssid"] = ssid
					map["password"] = pass
					if (dnsServers != null) map["dnsServers"] = dnsServers
					Handler(Looper.getMainLooper()).post { result.success(map) }
				}

				override fun onStopped() {
					super.onStopped()
					Handler(Looper.getMainLooper()).post { result.error("STOPPED", "Hotspot stopped", null) }
				}

				override fun onFailed(reason: Int) {
					super.onFailed(reason)
					Handler(Looper.getMainLooper()).post { result.error("FAILED", "startLocalOnlyHotspot failed: $reason", null) }
				}
			}, Handler(Looper.getMainLooper()))
		} catch (e: Exception) {
			Log.e("PocketFence", "startLocalOnlyHotspot exception", e)
			result.error("EXCEPTION", "Exception starting hotspot: ${e.message}", null)
		}
	}

	/**
	 * Close the active LocalOnlyHotspot reservation, if any.
	 */
	private fun stopLocalOnlyHotspot(result: MethodChannel.Result) {
		try {
			hotspotReservation?.close()
			hotspotReservation = null
			result.success(true)
		} catch (e: Exception) {
			result.error("EXCEPTION", "Exception stopping hotspot: ${e.message}", null)
		}
	}

	override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<String>, grantResults: IntArray) {
		super.onRequestPermissionsResult(requestCode, permissions, grantResults)
		if (requestCode == REQ_NEARBY) {
			var allGranted = true
			for (r in grantResults) {
				if (r != PackageManager.PERMISSION_GRANTED) {
					allGranted = false
					break
				}
			}
			val r = pendingResult
			val name = pendingName ?: "PocketFence"
			val block = pendingBlock
			val dns = pendingDnsServers
			// clear pending
			pendingResult = null
			pendingName = null
			pendingBlock = false
			pendingDnsServers = null
			if (allGranted) {
				if (r != null) startLocalOnlyHotspotInternal(name, block, dns, r)
			} else {
				r?.error("MISSING_PERMISSION", "Required nearby/bluetooth permissions were not granted.", null)
			}
		}
	}

	/**
	 * Heuristic to detect emulator images. Used ONLY to relax permission
	 * checks when running in an emulator for local testing.
	 */
	private fun isProbablyEmulator(): Boolean {
		val fingerprint = Build.FINGERPRINT ?: ""
		val model = Build.MODEL ?: ""
		val product = Build.PRODUCT ?: ""
		val manufacturer = Build.MANUFACTURER ?: ""
		return fingerprint.startsWith("generic") || fingerprint.startsWith("unknown") ||
				model.contains("google_sdk") || model.contains("Emulator") || model.contains("Android SDK built for x86") ||
				product == "sdk" || product == "google_sdk" || product.contains("sdk_gphone") ||
				manufacturer.contains("Genymotion")
	}
}


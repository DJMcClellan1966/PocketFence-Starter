package com.example.pocketfence

import android.os.Build
import android.Manifest
import android.content.Context
import android.content.pm.PackageManager
import android.os.Handler
import android.os.Looper
import android.util.Log
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat
import io.flutter.embedding.android.FlutterActivity
import io.flutter.plugin.common.MethodChannel
import android.net.wifi.WifiManager
import android.net.wifi.WifiManager.LocalOnlyHotspotReservation

/**
 * MainActivity provides a small bridge between Flutter and Android's
 * LocalOnlyHotspot API. It exposes a `pocketfence.hotspot` method channel
 * with `startHotspot` and `stopHotspot` methods.
 *
 * Notes and rationale:
 * - Android 13+ (Tiramisu) introduces `NEARBY_DEVICES` and Bluetooth
 *   runtime permissions which some hotspot APIs require. We request those
 *   at runtime and resume the operation if granted.
 * - Emulators / system images may not allow granting `NEARBY_DEVICES`; in
 *   that case we detect the emulator and skip the check to allow local
 *   testing. Do NOT rely on this bypass in production.
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
				"setHotspotName" -> result.error("UNIMPLEMENTED", "setHotspotName not implemented on Android. See docs/mobile_hotspot_native.md", null)
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
			Log.w("PocketFence", "Emulator detected — skipping NEARBY/BLUETOOTH runtime checks for testing")
			startLocalOnlyHotspotInternal(requestedName, blockOthers, dnsServers, result)
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
}

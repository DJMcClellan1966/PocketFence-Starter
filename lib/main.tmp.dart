import 'dart:io';

import 'package:flutter/material.dart';
import 'package:permission_handler/permission_handler.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:flutter/services.dart';

const MethodChannel _channel = MethodChannel('pocketfence.hotspot');

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  runApp(const MyApp());
}

class MyApp extends StatefulWidget {
  const MyApp({super.key});

  @override
  State<MyApp> createState() => _MyAppState();
}

class _MyAppState extends State<MyApp> {
  final TextEditingController _ssidController = TextEditingController();
  bool _blockOthers = true;
  bool _hotspotRunning = false;
  String _status = 'idle';

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _promptPermissionIfNeeded());
    _loadSaved();
  }

  Future<void> _loadSaved() async {
    final prefs = await SharedPreferences.getInstance();
    _ssidController.text = prefs.getString('hotspot_ssid') ?? '';
    _blockOthers = prefs.getBool('hotspot_block') ?? true;
    if (!mounted) return;
    setState(() {});
  }

  Future<void> _promptPermissionIfNeeded() async {
    if (Platform.isAndroid || Platform.isIOS) {
      final status = await Permission.location.status;
      if (!status.isGranted) {
        if (!mounted) return;
        await showDialog<void>(
          context: context,
          builder: (context) => AlertDialog(
            title: const Text('Location permission required'),
            content: const Text('This app needs location permission to start the hotspot on mobile devices.'),
            actions: [
              TextButton(onPressed: () => Navigator.of(context).pop(), child: const Text('Cancel')),
              TextButton(
                onPressed: () async {
                  Navigator.of(context).pop();
                  await Permission.location.request();
                },
                child: const Text('Allow'),
              ),
            ],
          ),
        );
      }
    }
  }

  Future<void> _startHotspot() async {
    setState(() => _status = 'starting');

    if (Platform.isAndroid || Platform.isIOS) {
      try {
        final prefs = await SharedPreferences.getInstance();
        await prefs.setString('hotspot_ssid', _ssidController.text);
        await prefs.setBool('hotspot_block', _blockOthers);

        await _channel.invokeMethod('startHotspot', {
          'ssid': _ssidController.text,
          'blockOthers': _blockOthers,
        });

        setState(() {
          _hotspotRunning = true;
          _status = 'running';
        });
      } on PlatformException catch (e) {
        setState(() => _status = 'error: ${e.message}');
      }
    } else {
      setState(() => _status = 'unsupported platform');
    }
  }

  Future<void> _stopHotspot() async {
    setState(() => _status = 'stopping');
    if (Platform.isAndroid || Platform.isIOS) {
      try {
        await _channel.invokeMethod('stopHotspot');
        setState(() {
          _hotspotRunning = false;
          _status = 'stopped';
        });
      } on PlatformException catch (e) {
        setState(() => _status = 'error: ${e.message}');
      }
    } else {
      setState(() => _status = 'unsupported platform');
    }
  }

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'PocketFence',
      home: Scaffold(
        appBar: AppBar(title: const Text('PocketFence Hotspot')),
        body: SafeArea(
          child: Padding(
            padding: const EdgeInsets.all(16.0),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Text('Hotspot SSID:'),
                TextField(controller: _ssidController),
                const SizedBox(height: 12),
                Row(
                  children: [
                    const Text('Block other Wi‑Fi'),
                    Switch(value: _blockOthers, onChanged: (v) => setState(() => _blockOthers = v)),
                  ],
                ),
                const SizedBox(height: 12),
                Row(
                  children: [
                    ElevatedButton(onPressed: _hotspotRunning ? null : _startHotspot, child: const Text('Start')),
                    const SizedBox(width: 12),
                    ElevatedButton(onPressed: _hotspotRunning ? _stopHotspot : null, child: const Text('Stop')),
                  ],
                ),
                const SizedBox(height: 20),
                Text('Status: $_status'),
                const SizedBox(height: 12),
                if (!Platform.isAndroid && !Platform.isIOS)
                  const Padding(
                    padding: EdgeInsets.only(top: 12.0),
                    child: Text('Hotspot control is available only on Android and iOS.'),
                  ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

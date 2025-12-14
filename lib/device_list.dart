import 'package:flutter/material.dart';
import 'device_discovery.dart';

class DeviceListPage extends StatefulWidget {
  const DeviceListPage({super.key});

  @override
  State<DeviceListPage> createState() => _DeviceListPageState();
}

class _DeviceListPageState extends State<DeviceListPage> {
  List<Map<String, dynamic>> _devices = [];
  final Set<String> _selected = {};
  bool _loading = true;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    final list = await DeviceDiscovery.listDevices();
    setState(() {
      _devices = list;
      _loading = false;
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Discovered Devices'),
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh),
            onPressed: _load,
          )
        ],
      ),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : _devices.isEmpty
              ? const Center(child: Text('No devices discovered'))
              : ListView.builder(
                  itemCount: _devices.length,
                  itemBuilder: (context, i) {
                    final d = _devices[i];
                    final id = d['mac'] ?? d['ip'] ?? i.toString();
                    final name = d['name'] ?? '${d['ip'] ?? 'unknown'}';
                    final platform = d['platform'] ?? 'unknown';
                    final selected = _selected.contains(id);
                    return ListTile(
                      leading: CircleAvatar(child: Text(platform.toString().substring(0,1).toUpperCase())),
                      title: Text(name.toString()),
                      subtitle: Text('IP: ${d['ip'] ?? '-'}  MAC: ${d['mac'] ?? '-'}'),
                      trailing: Checkbox(value: selected, onChanged: (v) {
                        setState(() {
                          if (v == true) {
                            _selected.add(id);
                          } else {
                            _selected.remove(id);
                          }
                        });
                      }),
                    );
                  },
                ),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => Navigator.of(context).pop(_selected.toList()),
        icon: const Icon(Icons.check),
        label: const Text('Select'),
      ),
    );
  }
}

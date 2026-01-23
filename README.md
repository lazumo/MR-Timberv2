TO generate a wood resource prefab:
```
ResourceHandlerNetworked handler = FindObjectOfType<ResourceHandlerNetworked>();
handler.SpawnResourceServerRpc(spawnPos);
```

TO change controller state (saw, box, juicer):
```
toolController.SetStateServerRpc(next);
```
using System.Text.Json;
using Forja.Anvil;
using Forja.Anvil.Scene;

namespace Forja.Core.Editing;

/// <summary>
/// Coloca um dispositivo novo na cena. O id vem de
/// <see cref="SceneDocument.NextDeviceId"/> na hora de aplicar — determinístico
/// sob undo/redo, pois um id só é reatribuído se o device anterior sumiu.
/// </summary>
public sealed class PlaceDeviceCommand : IEditorCommand
{
    private readonly string _typeId;
    private readonly Pose _pose;

    public PlaceDeviceCommand(string typeId, Pose pose)
    {
        _typeId = typeId;
        _pose = pose;
    }

    public string Label => $"Adicionar {_typeId}";

    public SceneDocument Apply(SceneDocument doc)
    {
        var devices = new List<DeviceInstance>(doc.Devices)
        {
            new DeviceInstance { Id = doc.NextDeviceId(), TypeId = _typeId, Transform = _pose },
        };
        return doc with { Devices = devices };
    }
}

/// <summary>Move um dispositivo (só a posição; rotação é comando à parte).</summary>
public sealed class MoveDeviceCommand : IEditorCommand
{
    private readonly uint _id;
    private readonly Vec3 _pos;

    public MoveDeviceCommand(uint id, Vec3 pos)
    {
        _id = id;
        _pos = pos;
    }

    public string Label => $"Mover dispositivo {_id}";

    public SceneDocument Apply(SceneDocument doc) => doc with
    {
        Devices = doc.Devices
            .Select(d => d.Id == _id ? d with { Transform = d.Transform with { Pos = _pos } } : d)
            .ToList(),
    };
}

/// <summary>Gira um dispositivo em torno de Y (graus).</summary>
public sealed class RotateDeviceCommand : IEditorCommand
{
    private readonly uint _id;
    private readonly float _rotY;

    public RotateDeviceCommand(uint id, float rotY)
    {
        _id = id;
        _rotY = rotY;
    }

    public string Label => $"Girar dispositivo {_id}";

    public SceneDocument Apply(SceneDocument doc) => doc with
    {
        Devices = doc.Devices
            .Select(d => d.Id == _id ? d with { Transform = d.Transform with { RotY = _rotY } } : d)
            .ToList(),
    };
}

/// <summary>
/// Remove os dispositivos selecionados e as tags de I/O órfãs que ficariam
/// (Artigo VI.1 — não deixa mapa apontando para device inexistente).
/// </summary>
public sealed class DeleteSelectionCommand : IEditorCommand
{
    private readonly HashSet<uint> _ids;

    public DeleteSelectionCommand(IEnumerable<uint> ids) => _ids = ids.ToHashSet();

    public string Label => $"Remover {_ids.Count} dispositivo(s)";

    public SceneDocument Apply(SceneDocument doc) => doc with
    {
        Devices = doc.Devices.Where(d => !_ids.Contains(d.Id)).ToList(),
        IoMap = doc.IoMap.Where(t => !_ids.Contains(t.DeviceId)).ToList(),
    };
}

/// <summary>
/// Duplica os selecionados com um deslocamento. As cópias recebem ids novos
/// sequenciais e NÃO herdam tags de I/O (endereços seriam duplicados — o
/// usuário mapeia as cópias na Tabela de I/O).
/// </summary>
public sealed class DuplicateSelectionCommand : IEditorCommand
{
    private readonly List<uint> _ids;
    private readonly Vec3 _offset;

    public DuplicateSelectionCommand(IEnumerable<uint> ids, Vec3 offset)
    {
        _ids = ids.ToList();
        _offset = offset;
    }

    public string Label => $"Duplicar {_ids.Count} dispositivo(s)";

    public SceneDocument Apply(SceneDocument doc)
    {
        var devices = new List<DeviceInstance>(doc.Devices);
        uint next = doc.NextDeviceId();

        foreach (var src in doc.Devices.Where(d => _ids.Contains(d.Id)).OrderBy(d => d.Id))
        {
            devices.Add(new DeviceInstance
            {
                Id = next++,
                TypeId = src.TypeId,
                Transform = src.Transform with { Pos = src.Transform.Pos + _offset },
                Params = new Dictionary<string, JsonElement>(src.Params),
            });
        }

        return doc with { Devices = devices };
    }
}

/// <summary>Edita um parâmetro de um dispositivo (data-driven — não valida
/// tipo aqui; a validação de faixa fica no painel/validador).</summary>
public sealed class EditParamCommand : IEditorCommand
{
    private readonly uint _id;
    private readonly string _name;
    private readonly JsonElement _value;

    public EditParamCommand(uint id, string name, JsonElement value)
    {
        _id = id;
        _name = name;
        _value = value;
    }

    public string Label => $"Editar '{_name}' do dispositivo {_id}";

    public SceneDocument Apply(SceneDocument doc) => doc with
    {
        Devices = doc.Devices.Select(d =>
        {
            if (d.Id != _id)
                return d;
            var pars = new Dictionary<string, JsonElement>(d.Params) { [_name] = _value };
            return d with { Params = pars };
        }).ToList(),
    };
}

/// <summary>Troca a configuração de conexão com o PLC (RF-06, T051). A
/// conexão é dado da cena (Artigo IV.2) — editar é comando como qualquer
/// outro, com undo.</summary>
public sealed class SetConnectionCommand : IEditorCommand
{
    private readonly ConnectionConfig _config;

    public SetConnectionCommand(ConnectionConfig config) => _config = config;

    public string Label => $"Conexão: driver '{_config.Driver}'";

    public SceneDocument Apply(SceneDocument doc) => doc with { Connection = _config };
}

/// <summary>
/// Reatribui o endereço de uma porta na Tabela de I/O (RF-05). Se a porta
/// ainda não tinha tag, cria uma. (Conflitos de endereço são pegos depois
/// pelo IoMapValidator ao tentar Run — Artigo VI.3.)
/// </summary>
public sealed class ReassignAddressCommand : IEditorCommand
{
    private readonly uint _deviceId;
    private readonly string _port;
    private readonly IoAddress _address;

    public ReassignAddressCommand(uint deviceId, string port, IoAddress address)
    {
        _deviceId = deviceId;
        _port = port;
        _address = address;
    }

    public string Label => $"Endereçar {_port} do dispositivo {_deviceId}";

    public SceneDocument Apply(SceneDocument doc)
    {
        var map = new List<IoTag>(doc.IoMap.Count);
        bool replaced = false;
        foreach (var tag in doc.IoMap)
        {
            if (tag.DeviceId == _deviceId && tag.PortName == _port)
            {
                map.Add(tag with { Address = _address });
                replaced = true;
            }
            else
            {
                map.Add(tag);
            }
        }
        if (!replaced)
            map.Add(new IoTag(_deviceId, _port, _address));

        return doc with { IoMap = map };
    }
}

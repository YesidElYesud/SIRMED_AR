using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "SirmedData", menuName = "ScriptableObjects/SirmedData", order = 1)]
public class SirmedDataScriptable : ScriptableObject ///Envoltorio de la respuesta de ambos endpoints
{
    public bool isUpToDate;
    public SirmedData data;
}

[Serializable]
public class SirmedData ///Envoltorio de la respuesta de ambos endpoints
{
    public int statusCode;
    public Layer layer;
}

[Serializable]
public class Layer ///Configuración de una capa del visor de Monitoreo y Alertas (registro de la tabla capas_monitoreo) junto con sus datos geográficos
{
    public int id; ///Identificador único de la capa ///1
    public string name; ///Nombre interno de la capa. Determina la tabla desde la que se cargan los datos //"Alarmas"
    public string display_name; ///Nombre con el que se muestra la capa (si es nulo se usa name) //"Alarmas"
    public string description; ///Descripción de la capa //"Capa de Alarmas"
    public string type; ///Tipo de capa. Valores: LOCAL, EXTERNAL ///"LOCAL"
#nullable enable
    public string? geometry; ///Tipo de geometría (points, lines, polygons) //"points"
    public string? url; ///Enlace de la capa en caso de ser externa //""
#nullable disable
    public bool active; ///Indica si la capa está activa //true
    public bool downloadable; ///Indica si la capa es descargable //true
    public List<Filter> filters; ///Filtros disponibles para la capa
    //public List<object> activeFilters; ///Filtros activos por defecto
    public string enlace_redireccion; ///Enlace de redirección de la capa //""
    public Styles styles; ///Estilos de visualización en el visor
    //public object? metadata; ///Metadatos de la capa (título, descripción, contactos, extensión, calidad, campos, etc.) //null
    public List<Datum> data; ///Registros geográficos de la capa. En Alarmas: registros de capa_alarmas
    //public object? pdf; ///Referencia al PDF con la metadata de la capa //null
#nullable enable
    public string? category; ///Categoría de la capa //"Monitoreo y Alertas"
#nullable disable
    public DateTime created_at; ///Fecha de creación de la capa (ISO 8601) //"2026-06-03T16:22:10.704Z"
}

[Serializable]
public class Filter ///Elemento del arreglo filters
{
    public string name; ///Etiqueta visible del filtro //"Comuna"
    public string type; ///Tipo de filtro (p. ej. text) //"text"
    public string property; ///Campo de data sobre el que se filtra //"comuna_nom"
}

[Serializable]
public class Styles ///unique: marcador cuadrado azul con ícono "plus" blanco. ///Objeto styles: simbología de puntos y polígonos
{
    public string type; ///Tipo de simbología (unique = un solo estilo) //"unique"
    public List<Punto> puntos;
    public List<Poligono> poligonos;

    public void ParseColors()
    {
        for (int i = 0; i < puntos.Count; i++)
        {
            ColorUtility.TryParseHtmlString(puntos[i].colorIcon, out puntos[i]._colorIcon);
            ColorUtility.TryParseHtmlString(puntos[i].markerColor, out puntos[i]._markerColor);
        }

        for (int i = 0; i < poligonos.Count; i++)
        {
            ColorUtility.TryParseHtmlString(poligonos[i].fillColor, out poligonos[i]._fillColor);
            ColorUtility.TryParseHtmlString(poligonos[i].lineColor, out poligonos[i]._lineColor);
        }
    }
}

[Serializable]
public class Punto
{
    public string prop; ///Propiedad y valor al que aplica el estilo (vacío = todos) //""
    public string value; //""
    public string colorIcon; ///Color del ícono (hexadecimal) //"#ffffff"
    public Color _colorIcon;
    public string markerIcon; ///Ícono del marcador //"plus"
    public string markerClass; ///Forma del marcador (p. ej. square) //"square"
    public string markerColor; ///Color del marcador //"#4338dc"
    public Color _markerColor;
}

[Serializable]
public class Poligono
{
    public string prop; ///Propiedad y valor al que aplica el estilo (vacío = todos) //""
    public string value; //""
    public string fillColor; ///Color de relleno (hexadecimal) //"#3388ff"
    public Color _fillColor;
    public string lineColor; ///Color del borde (hexadecimal) //"#3388ff"
    public Color _lineColor;
    public int lineWeight; ///Grosor del borde //2
    public double fillOpacity; ///Opacidad del relleno (0 a 1) //0.5
    public int lineOpacity; ///Opacidad del borde (0 a 1) //1
}

[Serializable]
public class Datum
{
    public int id; ///Identificador único del registro de la alarma //1
#nullable enable
    public string? modulo; ///Nombre del módulo/capa al que pertenece el registro dentro del visor //"alarmas"
    public string? siata_cod; ///Código identificador de la alarma en el sistema SIATA //"216"
    public string? siata_nom; ///Nombre de la alarma registrado en el sistema SIATA //"Santa Rita"
    public string? barrio_cod; ///Código del barrio donde se ubica la alarma //"8007"
    public string? barrio_nom; ///Nombre del barrio donde se ubica la alarma //"La Verde"
    public string? comuna_cod; ///Código de la comuna donde se ubica la alarma //"80"
    public string? comuna_nom; ///Nombre de la comuna donde se ubica la alarma //"San Antonio de Prado"
    public string? microcuenc; ///Nombre de la microcuenca asociada //"Quebrada Doña María"
    public string? nivel; ///Sensor de nivel asociado a la alarma //"108"
    public string? pluviograf; ///Pluviógrafo asociado a la alarma //"727"
    public string? camara; ///Cámara asociada a la alarma //"100637"
#pragma warning disable UAC1001
    public double? latitud; ///Coordenada geográfica de latitud (WGS84) //6.190698685042962
    public double? longitud; ///Coordenada geográfica de longitud (WGS84) //-75.65150023275535
#pragma warning restore UAC1001
#nullable disable
    public DateTime created_at; ///Fecha y hora de creación del registro (ISO 8601) //"2026-07-03T09:25:21.855Z"
}
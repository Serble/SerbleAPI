using System.Globalization;
using System.Numerics;

namespace SerbleAPI.Data.Schemas; 

public class Number {
    private double _value;
    public double DoubleValue {
        get => _value;
        set => _value = value;
    }
    public float FloatValue {
        get => (float)_value;
        set => _value = value;
    }
    public int IntValue {
        get => (int)_value;
        set => _value = value;
    }
    public static Number Instance = new();

    public Number(double val) { 
        _value = val;
    }

    public Number() { }

    public static implicit operator Number(int value) {
        return new Number(value);
    }
    
    public static implicit operator Number(double value) {
        return new Number(value);
    }
    
    public static implicit operator Number(float value) {
        return new Number(value);
    }
    
    public static implicit operator Number(byte value) {
        return new Number(value);
    }
    
    public static implicit operator Number(short value) {
        return new Number(value);
    }
    
    public static implicit operator Number(long value) {
        return new Number(value);
    }
    
    public static implicit operator Number(ulong value) {
        return new Number(value);
    }

    public static implicit operator float(Number value) {
        return value.FloatValue;
    }
    
    public static implicit operator double(Number value) {
        return value.DoubleValue;
    }
    
    public static implicit operator int(Number value) {
        return value.IntValue;
    }
    
    public static implicit operator byte(Number value) {
        return (byte)value.DoubleValue;
    }
    
    public static implicit operator short(Number value) {
        return (short)value.DoubleValue;
    }
    
    public static implicit operator long(Number value) {
        return (long)value.DoubleValue;
    }
    
    public static implicit operator ulong(Number value) {
        return (ulong)value.DoubleValue;
    }

    public static Number operator +(Number a, Number b) {
        return a._value + b._value;
    }
    
    public static Number operator -(Number a, Number b) {
        return a._value - b._value;
    }
    
    public static Number operator *(Number a, Number b) {
        return a._value * b._value;
    }
    
    public static Number operator /(Number a, Number b) {
        return a._value / b._value;
    }
    
    public static Number operator %(Number a, Number b) {
        return a._value % b._value;
    }
    
    public static Number operator ++(Number a) {
        return a._value++;
    }
    
    public static Number operator --(Number a) {
        return a._value--;
    }
    
    public static bool operator ==(Number a, Number b) {
        return a._value == b._value;
    }
    
    public static bool operator !=(Number a, Number b) {
        return a._value != b._value;
    }
    
    public static bool operator <(Number a, Number b) {
        return a._value < b._value;
    }
    
    public static bool operator >(Number a, Number b) {
        return a._value > b._value;
    }
    
    public static bool operator <=(Number a, Number b) {
        return a._value <= b._value;
    }
    
    public static bool operator >=(Number a, Number b) {
        return a._value >= b._value;
    }
    
    public override bool Equals(object obj) {
        return obj is Number number && _value == number._value;
    }
    
    public override int GetHashCode() {
        return _value.GetHashCode();
    }
    
    public override string ToString() {
        return _value.ToString();
    }
    
    public static Number Parse(string s) {
        return double.Parse(s);
    }
    
    public static Number Parse(string s, NumberStyles style) {
        return double.Parse(s, style);
    }
    
    public static Number Parse(string s, NumberStyles style, IFormatProvider provider) {
        return double.Parse(s, style, provider);
    }
    
    public static Number Parse(string s, IFormatProvider provider) {
        return double.Parse(s, provider);
    }
    
    public static bool TryParse(string s, out Number result) {
        result = 0;
        bool suc = double.TryParse(s, out double val);
        if (!suc) return false;
        result = val;
        return true;
    }
    
    public static bool TryParse(string s, NumberStyles style, IFormatProvider provider, out Number result) {
        result = 0;
        bool suc = double.TryParse(s, style, provider, out double val);
        if (!suc) return false;
        result = val;
        return true;
    }
    
    public static Vector2 ToVector2(Number x, Number y) {
        return new Vector2(x, y);
    }

}
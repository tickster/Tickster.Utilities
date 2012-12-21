Tickster.Utilities
==================

Tickster.Utilities is a project for utility-classes and functions that are frequently used across our projects. In here you will find a subset of our actual Utils-project including:

* ExceptionSignatureBuilder
* NaturalStringComparer
* SerializedResourcePool
* MemberwiseEqualityComparer
* Rfc822AddressValidator
* HexTranslator

## ExceptionSignatureBuilder
Given an Exception, builds a string-based signature for that exception. This is used by us to group exceptions in our error-logging system based on signature, which in turn gives us a good overview on how many times a particular exception has happened.

[Blog post](http://blog.freakcode.com/2009/07/introducing-exception-signatures.html)

## NaturalStringComparer
High performance, fully managed comparer for peforming natural sort (ie abc1 is sorted before "abc10"). White space is not significant for sorting (ie "abc 1" is equal to "abc1"). For highest performance initialize the comparer with StringComparison.Ordinal or StringComparison.OrdinalIgnoreCase.

## SerializedResourcePool
SerializedResourcePool provides threadsafe (via ReaderWriterLockSlim) access to a collection, with an optional automatic purging of objects.

## MemberwiseEqualityComparer
Provides an implementation of EqualityComparer that performs very fast memberwise equality comparison of objects.

Mirrored here, see [self-contained repo](https://github.com/markus-olsson/MemberwiseEqualityComparer)

## Rfc822AddressValidator
A high-performance email address validator that validates most email address formats specified in RFC 822. Outperforms several non-trivial (interpreted) regular expression based validation methods.

## HexTranslator
HexTranslator provides a smooth and performant way of moving between byte-arrays and hexadecimal strings.

## LICENSE
Tickster.Utilities is licensed under the [MIT License](https://github.com/tickster/Tickster.Utilities/blob/master/LICENSE.txt) ([OSI](http://www.opensource.org/licenses/mit-license.php)).
